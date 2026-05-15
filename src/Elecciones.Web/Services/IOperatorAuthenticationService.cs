namespace EleccionesWeb.Services;

public interface IOperatorAuthenticationService
{
    bool ValidateCredentials(string operatorId, string password);
}
