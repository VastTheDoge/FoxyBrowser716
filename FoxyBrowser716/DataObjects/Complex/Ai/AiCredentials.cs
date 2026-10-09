using Windows.Security.Credentials;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.DataObjects.Complex.Ai;

/// <summary>
/// AI provider API keys, kept in the Windows Credential Locker (Credential Manager → Web Credentials) rather than
/// the settings file. One key per provider, shared by every instance.
/// </summary>
public static class AiCredentials
{
	private const string Resource = "FoxyBrowser716 AI";

	public static string? Get(AiProviderKind provider)
	{
		try
		{
			var credential = new PasswordVault().Retrieve(Resource, provider.ToString());
			credential.RetrievePassword();
			return string.IsNullOrEmpty(credential.Password) ? null : credential.Password;
		}
		catch (Exception)
		{
			// Retrieve throws (Element not found) when no key was saved yet
			return null;
		}
	}

	/// <summary>Saves the key, or removes it when <paramref name="key"/> is empty.</summary>
	public static void Set(AiProviderKind provider, string? key)
	{
		key = key?.Trim();
		try
		{
			var vault = new PasswordVault();
			try
			{
				vault.Remove(vault.Retrieve(Resource, provider.ToString()));
			}
			catch (Exception)
			{
				// nothing saved yet
			}

			if (!string.IsNullOrEmpty(key))
				vault.Add(new PasswordCredential(Resource, provider.ToString(), key));
		}
		catch (Exception e)
		{
			// e.g. no Credential Locker (Wine)
			FoxyLogger.AddError(e);
		}
	}
}
