using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

public class ComplianceAlertDispatcher : IComplianceAlertDispatcher
{
    public event Action<Notification>? OnComplianceAlert;
    public event Action<object>? OnComplianceRenewed;

    public void DispatchComplianceAlert(Notification notification)
    {
        OnComplianceAlert?.Invoke(notification);
    }

    public void DispatchComplianceRenewed(object payload)
    {
        OnComplianceRenewed?.Invoke(payload);
    }
}
