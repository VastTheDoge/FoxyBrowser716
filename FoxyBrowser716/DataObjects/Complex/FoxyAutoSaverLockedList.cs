using FoxyBrowser716.DataManagement;

namespace FoxyBrowser716.DataObjects.Complex;

/// <summary>
/// An auto-saved list for data that changes often (history, downloads, site permissions).
/// Unlike <see cref="FoxyAutoSaverList{T}"/>, every read and write goes through a lock and saves serialize a
/// snapshot, so the auto-saver's timer thread never enumerates the list while the UI thread is changing it.
/// Items do not need to raise PropertyChanged; call <see cref="Mutate"/> (or <see cref="IFoxyAutoSaverItem.RequestSave"/>)
/// after changing one.
/// </summary>
public sealed class FoxyAutoSaverLockedList<T> : IFoxyAutoSaverItem where T : class
{
	private readonly object _sync = new();
	private readonly List<T> _items = [];
	private readonly string _filePath;
	private readonly SavePriority _priority;

	public bool IsLoaded { get; private set; }

	public FoxyAutoSaverLockedList(string fileName, FoxyFileManager.FolderType folderType, string? instanceName = null, SavePriority priority = SavePriority.Normal)
	{
		_filePath = FoxyFileManager.BuildFilePath(fileName, folderType, instanceName);
		_priority = priority;
	}

	/// <summary>Runs <paramref name="read"/> under the lock and returns its result. Do not keep the list reference.</summary>
	public TResult Read<TResult>(Func<List<T>, TResult> read)
	{
		lock (_sync)
			return read(_items);
	}

	/// <summary>Changes the list under the lock, then queues a save.</summary>
	public void Mutate(Action<List<T>> mutate)
	{
		lock (_sync)
			mutate(_items);

		if (IsLoaded)
			RequestSave(_priority);
	}

	public T[] Snapshot() => Read(items => items.ToArray());

	internal override async Task Save()
	{
		if (!IsLoaded) return;

		var result = await FoxyFileManager.SaveToFileAsync(_filePath, Snapshot());

		if (result != FoxyFileManager.ReturnCode.Success)
			throw new Exception($"Failed to save {_filePath}: {result}");
	}

	internal override async Task Load()
	{
		T[]? loaded = null;
		try
		{
			var result = await FoxyFileManager.ReadFromFileAsync<T[]>(_filePath);

			if (result.code != FoxyFileManager.ReturnCode.Success && result.code != FoxyFileManager.ReturnCode.NotFound)
				throw new Exception($"Failed to load {_filePath}: {result.code}");

			loaded = result.content;
		}
		catch (Exception e)
		{
			// This data is not worth failing startup over: keep the unreadable file aside and start empty.
			ErrorHandeler.FoxyLogger.AddError(e);
			try { File.Copy(_filePath, _filePath + ".unreadable", true); } catch { /* best effort */ }
		}

		lock (_sync)
		{
			_items.Clear();
			if (loaded is not null)
				_items.AddRange(loaded.Where(i => i is not null));
		}

		IsLoaded = true;
	}
}
