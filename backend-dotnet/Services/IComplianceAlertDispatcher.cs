using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

public interface IComplianceAlertDispatcher
{
    event Action<Notification>? OnComplianceAlert;
    event Action<object>? OnComplianceRenewed;
    void DispatchComplianceAlert(Notification notification);
    void DispatchComplianceRenewed(object payload);
}
