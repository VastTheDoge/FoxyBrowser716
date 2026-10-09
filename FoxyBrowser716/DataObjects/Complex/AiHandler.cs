using FoxyBrowser716.Controls.MainWindow;

namespace FoxyBrowser716.DataObjects.Complex;

/// <summary>The chats of one window's assistant panel. Chats live for the session (no saved history yet).</summary>
public class AiHandler(MainWindow mainWindow)
{
	//TODO: load and save chat history
	private readonly Dictionary<int, AiChat> _chats = [];
	public ReadOnlyDictionary<int, AiChat> Chats => _chats.AsReadOnly();

	public int ActiveChatId { get; private set; } = -1;
	public AiChat? ActiveChat => ActiveChatId == -1 ? null : _chats.GetValueOrDefault(ActiveChatId);

	/// <summary>Starts a chat with the provider currently picked in settings and makes it active.</summary>
	/// <param name="permissionMode">Null: the default from settings.</param>
	public AiChat NewChat(Ai.AiPermissionMode? permissionMode = null)
	{
		var settings = mainWindow.Instance.Settings;
		var chat = new AiChat(mainWindow, settings.AiProvider, permissionMode ?? settings.AiPermissionMode);
		_chats.Add(chat.Id, chat);
		ActiveChatId = chat.Id;
		return chat;
	}

	public void SwapChat(int id)
	{
		if (_chats.ContainsKey(id)) ActiveChatId = id;
	}

	public void DeleteChat(int id)
	{
		_chats.Remove(id);
		if (ActiveChatId == id) ActiveChatId = -1;
	}

	/// <summary>Tokens and estimated cost across every chat in this window.</summary>
	public Ai.AiUsage TotalUsage => _chats.Values.Aggregate(Ai.AiUsage.Zero, (total, chat) => total.Add(chat.Usage));
}
