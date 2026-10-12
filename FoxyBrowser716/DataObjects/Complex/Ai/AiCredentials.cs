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

	/// <summary>
	/// The key's start, end and length, never the whole thing: enough to compare against the Console and spot a
	/// wrong or cut-off paste. "sk-ant-api03-…x9Qk (108 characters)".
	/// </summary>
	public static string Mask(string? key)
	{
		if (string.IsNullOrEmpty(key)) return "no key";
		return key.Length <= 20
			? $"{key.Length} characters"
			: $"{key[..13]}…{key[^4..]} ({key.Length} characters)";
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
