using Minch.Core.Services;
using Windows.Security.Credentials;

namespace Minch.App.Services;

/// <summary>Stores secrets in the Windows Credential Locker (per-user, DPAPI-protected).</summary>
public sealed class CredentialLockerSecretStore : ISecretStore
{
    private const string Resource = "Minch";
    private readonly PasswordVault _vault = new();

    public string? Read(string key)
    {
        try
        {
            var cred = _vault.Retrieve(Resource, key);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch (Exception)
        {
            // Retrieve throws when nothing is stored.
            return null;
        }
    }

    public void Write(string key, string value)
    {
        Delete(key);
        _vault.Add(new PasswordCredential(Resource, key, value));
    }

    public void Delete(string key)
    {
        try
        {
            foreach (var c in _vault.FindAllByResource(Resource))
                if (c.UserName == key) _vault.Remove(c);
        }
        catch (Exception)
        {
            // FindAllByResource throws when the resource has no entries.
        }
    }
}
