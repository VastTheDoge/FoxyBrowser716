using System.Threading;
using FoxyBrowser716.Controls.MainWindow;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Complex.Ai;
using FoxyBrowser716.ErrorHandeler;

namespace FoxyBrowser716.DataObjects.Complex;

// Claude Doc
/// <summary>
/// The chats of one window's assistant panel. Chats are saved per instance (one file each in its data folder,
/// written after every finished turn), so every window of the instance lists them and they survive restarts.
/// </summary>
public class AiHandler(MainWindow mainWindow)
{
	private readonly Dictionary<int, AiChat> _chats = [];
	public ReadOnlyDictionary<int, AiChat> Chats => _chats.AsReadOnly();

	public int ActiveChatId { get; private set; } = -1;
	public AiChat? ActiveChat => ActiveChatId == -1 ? null : _chats.GetValueOrDefault(ActiveChatId);

	private readonly SemaphoreSlim _writeLock = new(1, 1);

	private string Folder => Path.Combine(FoxyFileManager.BuildFolderPath(FoxyFileManager.FolderType.Data, mainWindow.Instance.Name), "AiChats");
	private string FileFor(int id) => Path.Combine(Folder, $"{id}.json");

	public async Task LoadSaved()
	{
		var (code, items) = await FoxyFileManager.GetChildrenOfFolderAsync(Folder, FoxyFileManager.ItemType.File);
		if (code != FoxyFileManager.ReturnCode.Success || items is null) return;

		foreach (var (_, path) in items.Where(i => i.path.EndsWith(".json")))
		{
			try
			{
				var (readCode, save) = await FoxyFileManager.ReadFromFileAsync<AiChatSave>(path);
				if (readCode != FoxyFileManager.ReturnCode.Success || save is null || _chats.ContainsKey(save.Id)) continue;
				_chats[save.Id] = AiChat.Load(mainWindow, save);
			}
			catch (Exception e)
			{
				//C one unreadable file shouldn't hide the other chats
				FoxyLogger.AddError(e);
			}
		}
	}

	public async void Save(AiChat chat)
	{
		if (chat.Transcript.Count == 0 || !mainWindow.Instance.Settings.AiSaveChatsEffective) return;
		var save = chat.ToSave();
		await _writeLock.WaitAsync();
		try
		{
			await FoxyFileManager.SaveToFileAsync(FileFor(chat.Id), save);
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	// Claude Doc
	/// <summary>Starts a chat and makes it active.</summary>
	/// <param name="model">Null: the default from settings.</param>
	/// <param name="permissionMode">Null: the default from settings.</param>
	public AiChat NewChat(AiModelChoice? model = null, AiPermissionMode? permissionMode = null)
	{
		var settings = mainWindow.Instance.Settings;
		var chat = new AiChat(mainWindow, model ?? settings.AiModel, settings.AiEffort, permissionMode ?? settings.AiPermissionMode);
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
		FoxyFileManager.DeleteFile(FileFor(id));
	}

	public AiUsage TotalUsage => _chats.Values.Aggregate(AiUsage.Zero, (total, chat) => total.Add(chat.Usage));
}
