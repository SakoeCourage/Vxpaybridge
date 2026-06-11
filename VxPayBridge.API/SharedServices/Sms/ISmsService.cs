namespace VxPayBridge.API.SharedServices.Sms;

public interface ISmsService
{
    Task SendSmsAsync(string phoneNumber, string message);
    string QueueSms(string phoneNumber, string message);
}
