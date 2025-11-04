namespace ZenCloud.Data.Entities;

public enum WebhookLogStatus
{
    Success = 1,
    Failed = 2,
    Pending = 3, // Optional: for queued webhooks not yet sent
    Retrying = 4 // Optional: for webhooks in retry queue
}