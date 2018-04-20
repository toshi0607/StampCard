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
        // ���o�C���N���C�A���g����̏��F���N�G�X�g���󂯁AOrchestrator���N�����邽�߂�OrchestrationClient�֐�
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
        // ���F�t���[�̃X�e�[�g���Ǘ�����Orchectrator�֐�
        [FunctionName("ApprovalFlowController")]
        public static async Task Run(
            [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<ApprovalRequest>();
            if (!context.IsReplaying) log.Info($"input: {input}");
            input.InstanceId = context.InstanceId;
            // Activity���Ăяo�����F�҂Ƀ��[���𑗐M����
            await context.CallActivityAsync("SendRequestApprovalMail", input);

            // Orchestration Client���N������Approval�C�x���g�𑗐M���邽�߂�URL�����[���ɏ�����Ă���̂ŁA
            // ���̃C�x���g�����M��ҋ@����
            bool approved = await context.WaitForExternalEvent<bool>("Approval");
            if (!context.IsReplaying) log.Info($"approved: {approved}");

            // Mobile Apps�̃G���h�|�C���g�ƂȂ�URL
            var stampCardURL = ConfigurationManager.AppSettings.Get("StampCardURL");
            var client = new MobileServiceClient(stampCardURL);
            var calendarDateTable = client.GetTable<CalendarDate>();
            IEnumerable<CalendarDate> cDates = await calendarDateTable.ToEnumerableAsync();
            // �ق�Ƃ̓N�G������Ƃ��ɍi�肽�����������t�ł��܂��i��Ȃ������̂ŋ�������...
            var update = cDates.Where(cDate => cDate.StampAt.Year == input.CalendarDate.Year &&
                cDate.StampAt.Month == input.CalendarDate.Month && cDate.StampAt.Date == input.CalendarDate.Date).Single();
            log.Info($"update: {update}");

            // ���[���N���b�N���̃N�G���p�����[�^��
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

        // ���F�҂����F�E�p�������߁AOrchectrator���ĊJ�����邽�߂�URL�������ꂽ���[���𑗐M���邽�߂�Activity�֐�
        // Orchectrator�����̊֐����Ăяo��
        [FunctionName("SendRequestApprovalMail")]
        // SendGridKey��
        // ���[�J����: local.settings.json �ɐݒ�
        // �{��/�X�e�[�W���O��: �A�v���P�[�V�����ݒ� �ɐݒ�
        [return: SendGrid(ApiKey = "SendGridKey", From = "info@stampcard.com")]
        public static Mail RequestApproval([ActivityTrigger] ApprovalRequest approvalRequest, TraceWriter log)
        {
            // Orchestrator���ĊJ���邽�߂�Orchestration Client�̃G���h�|�C���g�ƂȂ�URL
            var approveURL = ConfigurationManager.AppSettings.Get("APPROVE_URL");

            // ���F�҂̃��[���B�T���v���Ȃ̂ŌŒ�ɂ��Ă��邪�A���F�ҏ���DB�ɕۑ����Ă�����擾����ׂ�
            var email = ConfigurationManager.AppSettings.Get("APPROVER_EMAIL");
            var message = new Mail
            {
                Subject = $"{approvalRequest.CalendarDate.ToString("yyyy/MM/dd")}�̃X�^���v���N�G�X�g"
            };

            Content content = new Content
            {
                Type = "text/plain",
                Value = "���F����ɂ͂���URL���N���b�N���Ă��������B\n\n" +
                    $"{approveURL}&isApproved=true&instanceId={approvalRequest.InstanceId}\n\n\n" +
                    "�p������ɂ͂���URL���N���b�N���Ă��������B\n\n" +
                    $"{approveURL}&isApproved=false&instanceId={approvalRequest.InstanceId}"
            };
            message.AddContent(content);
            var personalization = new Personalization();
            personalization.AddTo(new Email(email));
            message.AddPersonalization(personalization);
            return message;
        }
    }

    // Orchestrator����̃��N�G�X�g���f�V���A���C�Y����
    public class ApprovalRequest
    {
        // ���N���������̃X�^���v���t��
        [JsonProperty(PropertyName = "calendarDate")]
        public DateTime CalendarDate { get; set; }
        // Approval�C�x���g��҂��󂯂Ă���Orchestrator����肷��
        public string InstanceId { get; set; }
    }

    // �X�^���v��1�����B���o�C���N���C�A���g�ł����̃N���X���g�p���ăX�^���v�f�[�^���X�V����
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
        // Approval�C�x���g�𑗐M����Orchectrator���ĊJ�����邽�߂�Orchestration Client
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

            var result = (isApproved == "true") ? "���F" : "�p��";

            try
            {
                await client.RaiseEventAsync(instanceId, "Approval", isApproved);
                return req.CreateResponse(HttpStatusCode.OK, $"�X�^���v���N�G�X�g��{result}���܂����B");
            }
            catch
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, "�X�^���v���N�G�X�g�̏����Ɏ��s���܂����B���Ԃ������čēx���������������B");
            }
        }
    }
}
