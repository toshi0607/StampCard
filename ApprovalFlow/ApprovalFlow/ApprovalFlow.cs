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
    public static class ApprovalFlowController
    {
        [FunctionName("ApprovalFlowController")]
        public static async Task Run(
            [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<ApprovalRequest>();
            if (!context.IsReplaying) log.Info($"input: {input}");
            input.InstanceId = context.InstanceId;
            await context.CallActivityAsync("RequestApproval", input);

            bool approved = await context.WaitForExternalEvent<bool>("Approval");
            if (!context.IsReplaying) log.Info($"approved: {approved}");

            var stampCardURL = ConfigurationManager.AppSettings.Get("StampCardURL");
            var client = new MobileServiceClient(stampCardURL);
            var calendarDateTable = client.GetTable<CalendarDate>();
            IEnumerable<CalendarDate> cDates = await calendarDateTable.ToEnumerableAsync();
            var update = cDates.Where(cDate => cDate.StampAt.Year == input.CalendarDate.Year &&
                cDate.StampAt.Month == input.CalendarDate.Month && cDate.StampAt.Date == input.CalendarDate.Date).Single();
            log.Info($"update: {update}");

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

        [FunctionName("RequestApproval")]
        [return: SendGrid(ApiKey = "SendGridKey", From = "info@stampcard.com")]
        public static Mail RequestApproval([ActivityTrigger] ApprovalRequest approvalRequest, TraceWriter log)
        {
            var approveURL = ConfigurationManager.AppSettings.Get("APPROVE_URL");

            var email = ConfigurationManager.AppSettings.Get("AUTHORIZER_EMAIL");
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

    public class ApprovalRequest
    {
        [JsonProperty(PropertyName = "calendarDate")]
        public DateTime CalendarDate { get; set; }
        public string InstanceId { get; set; }
    }

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

    public static class Approval
    {
        [FunctionName("Approval")]
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

    public static class HttpStart
    {
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
}
