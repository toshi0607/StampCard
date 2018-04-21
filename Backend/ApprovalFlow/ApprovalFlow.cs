using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Microsoft.WindowsAzure.MobileServices;
using System.Configuration;
using System.Net;
using SendGrid.Helpers.Mail;
using SendGrid.Helpers;

namespace ApprovalFlow
{
    public static class HttpStart
    {
        // モバイルクライアントからの承認リクエストを受け、Orchestratorを起動するためのOrchestrationClient関数
        [FunctionName("HttpStart")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, methods: "post", Route = "orchestrators/{functionName}")] HttpRequestMessage req,
            [OrchestrationClient] DurableOrchestrationClient starter,
            string functionName,
            TraceWriter log)
        {
            log.Info($"starter: {req}");
            dynamic eventData = await req.Content.ReadAsAsync<object>();
            log.Info($"starter:  eventData {eventData}");
            string instanceId = await starter.StartNewAsync(functionName, eventData);

            log.Info($"Started orchestration with ID = '{instanceId}'.");

            var res = starter.CreateCheckStatusResponse(req, instanceId);
            log.Info($"starter: res {res}");

            res.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return res;
        }
    }

    public static class ApprovalFlowController
    {
        // 承認フローのステートを管理するOrchectrator関数
        [FunctionName("ApprovalFlowController")]
        public static async Task Run(
            [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<ApprovalRequest>();
            if (!context.IsReplaying) log.Info($"input: {input}");
            input.InstanceId = context.InstanceId;
            // Activityを呼び出し承認者にメールを送信する
            await context.CallActivityAsync("SendRequestApprovalMail", input);

            // Orchestration Clientを起動してApprovalイベントを送信するためのURLがメールに書かれているので、
            // そのイベントが送信を待機する。
            // ただ、Approvalイベントだけ待つとメール送信失敗時に外部から更新できなくなってしまうので、
            // タイマーでタイムアウトを設定して、タイムアウトするときにReviewingステータスを何もないステータスに変更して
            // クライアントから再度承認リクエストできるようにした方がうよさそう。
            // （外部イベントとタイムアウトの先勝ちで処理を進める）
            bool approved = await context.WaitForExternalEvent<bool>("Approval");
            if (!context.IsReplaying) log.Info($"approved: {approved}");

            // Mobile AppsのエンドポイントとなるURL
            var stampCardURL = ConfigurationManager.AppSettings.Get("StampCardURL");
            var client = new MobileServiceClient(stampCardURL);
            var calendarDateTable = client.GetTable<CalendarDate>();
            IEnumerable<CalendarDate> cDates = await calendarDateTable.ToEnumerableAsync();
            // ほんとはクエリするときに絞りたかったが日付でうまく絞れなかったので泣く泣く...
            var update = cDates.Where(cDate => cDate.StampAt.Year == input.CalendarDate.Year &&
                cDate.StampAt.Month == input.CalendarDate.Month && cDate.StampAt.Date == input.CalendarDate.Date).Single();
            log.Info($"update: {update}");

            // メールクリック時のクエリパラメータに
            if (approved)
            {
                update.Type = CalendarDate.Status.Approved;
                log.Info($"approved update: {update}");
                await client.GetTable<CalendarDate>().UpdateAsync(update);
            }
            else
            {
                update.Type = CalendarDate.Status.Rejected;
                log.Info($"rejected update: {update}");
                await client.GetTable<CalendarDate>().UpdateAsync(update);
            }
        }

        // 承認者が承認・却下を決め、Orchectratorを再開させるためのURLが書かれたメールを送信するためのActivity関数
        // Orchectratorがこの関数を呼び出す
        [FunctionName("SendRequestApprovalMail")]
        // SendGridKeyは
        // ローカル環境: local.settings.json に設定
        // 本番/ステージング環境: アプリケーション設定 に設定
        [return: SendGrid(ApiKey = "SendGridKey", From = "info@stampcard.com")]
        public static Mail RequestApproval([ActivityTrigger] ApprovalRequest approvalRequest, TraceWriter log)
        {
            // Orchestratorを再開するためのOrchestration ClientのエンドポイントとなるURL
            var approveURL = ConfigurationManager.AppSettings.Get("APPROVE_URL");

            // 承認者のメール。サンプルなので固定にしているが、承認者情報をDBに保存してそれを取得するべき
            var email = ConfigurationManager.AppSettings.Get("APPROVER_EMAIL");
            var message = new Mail
            {
                Subject = $"{approvalRequest.CalendarDate.ToString("yyyy/MM/dd")}のスタンプリクエスト"
            };

            Content content = new Content
            {
                Type = "text/plain",
                Value = "承認するにはつぎのURLをクリックしてください。\n\n" +
                    $"{approveURL}&isApproved=true&instanceId={approvalRequest.InstanceId}\n\n\n" +
                    "却下するにはつぎのURLをクリックしてください。\n\n" +
                    $"{approveURL}&isApproved=false&instanceId={approvalRequest.InstanceId}"
            };
            message.AddContent(content);
            var personalization = new Personalization();
            personalization.AddTo(new Email(email));
            message.AddPersonalization(personalization);
            return message;
        }
    }

    // Orchestratorからのリクエストをデシリアライズする
    public class ApprovalRequest
    {
        // 何年何月何日のスタンプ日付か
        [JsonProperty(PropertyName = "calendarDate")]
        public DateTime CalendarDate { get; set; }
        // Approvalイベントを待ち受けているOrchestratorを特定する
        public string InstanceId { get; set; }
    }

    // スタンプの1日分。モバイルクライアントでもこのクラスを使用してスタンプデータを更新する
    public class CalendarDate
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }
        public enum Status
        {
            Reviewing,
            Approved,
            Rejected,
        }
        [JsonConverter(typeof(StringEnumConverter))]
        public Status Type { get; set; }

        [JsonProperty(PropertyName = "stampAt")]
        public DateTime StampAt { get; set; }
    }

    public static class ApprovalEventSender
    {
        // Approvalイベントを送信してOrchectratorを再開させるためのOrchestration Client
        [FunctionName("ApprovalEventSender")]
        public static async Task<HttpResponseMessage> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestMessage req,
        [OrchestrationClient] DurableOrchestrationClient client)
        {
            dynamic eventData = await req.Content.ReadAsAsync<object>();

            string instanceId = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "instanceId", true) == 0)
                .Value;

            string isApproved = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "isApproved", true) == 0)
                .Value;

            var result = (isApproved == "true") ? "承認" : "却下";

            try
            {
                await client.RaiseEventAsync(instanceId, "Approval", isApproved);
                return req.CreateResponse(HttpStatusCode.OK, $"スタンプリクエストを{result}しました。");
            }
            catch
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "スタンプリクエストの処理に失敗しました。時間をおいて再度お試しください。");
            }
        }
    }
}
