using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using Windows.ApplicationModel.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using FoxyBrowser716.DataManagement;

namespace FoxyBrowser716.DataObjects.Complex;


public abstract class IFoxyAutoSaverItem
{
	internal event Action<IFoxyAutoSaverItem, SavePriority>? SaveRequested;
	
	private SavePriority Priority { get; init; } = SavePriority.Normal;
	private string FilePath { get; init; }
	internal abstract Task Save();
	internal abstract Task Load();

	public virtual void RequestSave(SavePriority? priority)
	{
		SaveRequested?.Invoke(this, priority ?? Priority);
	}
	// public abstract void RequestLoad();
}

/// <summary>
/// TODO: make better summary.
///
/// This should act as the field itself! pass in new T()
/// </summary>
/// <typeparam name="T"></typeparam>
public class FoxyAutoSaverField<T> : IFoxyAutoSaverItem where T : class, INotifyPropertyChanged, new()
{
	private SavePriority Priority { get; }
	private string FilePath { get; init; }
	public bool IsLoaded { get; private set; } = false;
	public T Item { get; private set; } = null!; // set via ItemFactory if null
	private Func<T> ItemFactory { get; init; }
	
	/// <summary>
	/// 
	/// </summary>
	/// <param name="itemFactory"></param>
	/// <param name="fileName"></param>
	/// <param name="folderType"></param>
	/// <param name="instanceName"></param>
	/// <param name="priority"></param>
	public FoxyAutoSaverField(Func<T> itemFactory, string fileName, FoxyFileManager.FolderType folderType, string? instanceName = null, SavePriority priority = SavePriority.Normal)
	{
		ItemFactory = itemFactory;
		FilePath = FoxyFileManager.BuildFilePath(fileName, folderType, instanceName);
		Priority = priority;
	}
	
	internal override async Task Save()
	{
		if (!IsLoaded) return;
		if (Item is null) return; // null before load
		
		var result = await FoxyFileManager.SaveToFileAsync(FilePath, Item);
		
		if (result != FoxyFileManager.ReturnCode.Success)
			throw new Exception($"Failed to save {FilePath}: {result}");
	}
	internal override async Task Load()
	{
		var result = await FoxyFileManager.ReadFromFileAsync<T>(FilePath);

		if (result.code == FoxyFileManager.ReturnCode.NotFound)
		{
			Item = ItemFactory();
			Item.PropertyChanged += HandlePropertyChanged;
			IsLoaded = true;
			return;
		}
		
		if (result.code != FoxyFileManager.ReturnCode.Success || result.content is null)
			throw new Exception($"Failed to load {FilePath}: {result}");
		
		Item = result.content;
		Item.PropertyChanged += HandlePropertyChanged;
		IsLoaded = true;
	}

	private void HandlePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (IsLoaded)
		{
			RequestSave(Priority);
		}
	}
}

[ObservableObject]
public partial class FoxyAutoSaverList<T> : IFoxyAutoSaverItem where T : class, INotifyPropertyChanged, new()
{
	private SavePriority Priority { get; init; } = SavePriority.Normal;
	protected string FilePath { get; init; }
	public bool IsLoaded { get; private set; } = false;
	
	[ObservableProperty] public partial ObservableCollection<T>? Items { get; private set; } = null;
	
	private readonly HashSet<T> _subscribedItems = [];

	public FoxyAutoSaverList(string fileName, FoxyFileManager.FolderType folderType, string? instanceName = null, SavePriority priority = SavePriority.Normal)
	{
		Items = [];
		FilePath = FoxyFileManager.BuildFilePath(fileName, folderType, instanceName);
		Priority = priority;
	}
	
	internal override async Task Save()
	{
		if (!IsLoaded) return;
		if (Items is null) return;
		
		var result = await FoxyFileManager.SaveToFileAsync(FilePath, Items);
		
		if (result != FoxyFileManager.ReturnCode.Success)
			throw new Exception($"Failed to save {FilePath}: {result}");
	}
	internal override async Task Load()
	{
		var result = await FoxyFileManager.ReadFromFileAsync<T[]>(FilePath);
		Items = [];
		
		if (result.code == FoxyFileManager.ReturnCode.NotFound)
		{
			Items.CollectionChanged += HandleCollectionChanged;
			IsLoaded = true;
			return;
		}
		
		if (result.code != FoxyFileManager.ReturnCode.Success || result.content is null)
			throw new Exception($"Failed to load {FilePath}: {result}");

		Items.CollectionChanged += HandleCollectionChanged;
		foreach (var item in result.content)
		{
			Items.Add(item);
		}
		IsLoaded = true;
	}

	private void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems is not null)
		{
			foreach (T item in e.NewItems)
			{
				if (_subscribedItems.Add(item))
				{
					item.PropertyChanged += HandlePropertyChanged;
				}
			}
		}
    
		if (e.OldItems is not null && e.Action != NotifyCollectionChangedAction.Move)
		{
			foreach (T item in e.OldItems)
			{
				if (_subscribedItems.Remove(item))
				{
					item.PropertyChanged -= HandlePropertyChanged;
				}
			}
		}
    
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (var item in _subscribedItems)
			{
				item.PropertyChanged -= HandlePropertyChanged;
			}
			_subscribedItems.Clear();
        
			if (Items is not null)
			{
				foreach (var item in Items)
				{
					if (_subscribedItems.Add(item))
					{
						item.PropertyChanged += HandlePropertyChanged;
					}
				}
			}
		}
    
		if (IsLoaded)
		{
			RequestSave(Priority);
		}
	}
	
	private void HandlePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (IsLoaded)
		{
			RequestSave(Priority);
		}
	}
}

public enum SavePriority
{
	Low,
	Normal,
	High,
	Immediate
}

public class FoxyAutoSaver : IDisposable
{
	/// <summary>
	/// In Milliseconds.
	/// high priority is every tick
	/// normal priority is every 5 ticks
	/// low priority is every 25 ticks
	/// </summary>
	public int SaveIntervalMs
	{
		get;
		set
		{
			field = value;
			_queueTimer.Interval = value;
		}
	} = 1000;

	private bool _runningTick = false;
	// no new objects to save while saving.
	private readonly ConcurrentQueue<(IFoxyAutoSaverItem item, SavePriority priority)> _waitQueue = [];
	
	private readonly ConcurrentQueue<IFoxyAutoSaverItem> _lowQueue = [];
	private readonly ConcurrentQueue<IFoxyAutoSaverItem> _normalQueue = [];
	private readonly ConcurrentQueue<IFoxyAutoSaverItem> _highQueue = [];

	private readonly Dictionary<IFoxyAutoSaverItem, SavePriority> _queuedItems = [];
	
	private readonly Timer _queueTimer = new();
	
	private readonly HashSet<IFoxyAutoSaverItem> _items = [];

	private FoxyAutoSaver()
	{ }

	//TODO: this event is not called, need to find another event to hook into
	private async void CoreApplicationOnExiting(object? sender, object e)
	{
		await Task.WhenAll(SaveQueue(_lowQueue, SavePriority.Low), SaveQueue(_normalQueue, SavePriority.Normal), SaveQueue(_highQueue, SavePriority.High));
	}

	public async Task<bool> AddItem(IFoxyAutoSaverItem item, bool loadItem = true)
	{
		if (!_items.Add(item)) return false;
		
		if (loadItem)
		{
			await item.Load();
		}
		
		item.SaveRequested += AddToQueue;
		return true;
	}
	
	public bool RemoveItem(IFoxyAutoSaverItem item)
	{
		if (!_items.Remove(item)) return false;
		
		item.SaveRequested -= AddToQueue;
		return true;
	}
	
	public async Task AddItems(List<IFoxyAutoSaverItem> items, bool loadItem = true)
	{
		await Task.WhenAll(items.Select(item => AddItem(item, loadItem)));
	}
	
	public void RemoveItems(List<IFoxyAutoSaverItem> items)
	{
		foreach (var item in items)
		{
			RemoveItem(item);
		}
	}

	
	public static async Task<FoxyAutoSaver> Create(List<IFoxyAutoSaverItem>? items = null, bool LoadItems = true)
	{
		items ??= [];
		
		var autoSaver = new FoxyAutoSaver();
		
		autoSaver._queueTimer.Interval = autoSaver.SaveIntervalMs;
		autoSaver._queueTimer.Elapsed += autoSaver.HandleQueueTimerElapsed;
		
		foreach (var item in items)
			if (autoSaver._items.Add(item))
				item.SaveRequested += autoSaver.AddToQueue;
		
		if (LoadItems)
		{
			foreach (var item in autoSaver._items)
			{
				await item.Load();
			}
		}

		autoSaver._queueTimer.Start();

		return autoSaver;
	}

	private int tick;

	/// <summary>
	/// Guards the queue bookkeeping (<see cref="_queuedItems"/>, <see cref="_runningTick"/>, the queues):
	/// save requests arrive on the UI thread while this timer runs on a thread-pool thread.
	/// </summary>
	private readonly object _queueLock = new();

	private async void HandleQueueTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		lock (_queueLock)
		{
			if (_runningTick)
				return;
			_runningTick = true;
		}

		List<Task> tasks = [];
		try
		{
			tasks.Add(SaveQueue(_highQueue, SavePriority.High));
			
			if (tick % 5 == 0)  // run every 5 ticks
				tasks.Add(SaveQueue(_normalQueue, SavePriority.Normal));
			
			if (tick % 25 == 0) // run every 25 ticks
			{
				tasks.Add(SaveQueue(_lowQueue, SavePriority.Low));
				tick = 0; // reset so that keeping the browser open forever does not cause a crash (due to integer limit).
			}

			tick += 1;
			
			await Task.WhenAll(tasks);
		}
		catch (Exception ex)
		{
			// this is an async void timer callback: an escaping exception would end the process
			ErrorHandeler.FoxyLogger.AddError(ex);
		}
		finally
		{
			List<IFoxyAutoSaverItem> immediate = [];
			lock (_queueLock)
			{
				// requests that came in while saving were parked; queue them now (and record them, or
				// SaveQueue would skip them)
				while (_waitQueue.TryDequeue(out var pair))
				{
					if (pair.priority == SavePriority.Immediate)
						immediate.Add(pair.item);
					else
						EnqueueLocked(pair.item, pair.priority);
				}

				_runningTick = false;
			}

			foreach (var item in immediate)
				_ = SaveSafely(item);
		}
	}
	
	private void AddToQueue(IFoxyAutoSaverItem item, SavePriority priority)
	{
		lock (_queueLock)
		{
			if (_runningTick)
			{
				_waitQueue.Enqueue((item, priority));
				return;
			}

			if (priority != SavePriority.Immediate)
			{
				EnqueueLocked(item, priority);
				return;
			}
		}

		_ = SaveSafely(item);
	}

	/// <summary>Caller holds <see cref="_queueLock"/>.</summary>
	private void EnqueueLocked(IFoxyAutoSaverItem item, SavePriority priority)
	{
		if (_queuedItems.TryGetValue(item, out var oldPriority) && oldPriority >= priority)
			return;
		
		switch (priority)
		{
			case SavePriority.Low:
				_lowQueue.Enqueue(item);
				break;
			case SavePriority.Normal:
				_normalQueue.Enqueue(item);
				break;
			case SavePriority.High:
				_highQueue.Enqueue(item);
				break;
		}

		_queuedItems[item] = priority;
	}
	
	private async Task SaveQueue(ConcurrentQueue<IFoxyAutoSaverItem> queue, SavePriority queuePriority)
	{
		List<IFoxyAutoSaverItem> toSave = [];
		lock (_queueLock)
		{
			while (queue.TryDequeue(out var item))
			{
				if (_queuedItems.TryGetValue(item, out var priority) && priority == queuePriority)
				{
					toSave.Add(item);
					_queuedItems.Remove(item);
				}
			}
		}
		await Task.WhenAll(toSave.Select(SaveSafely));
	}

	/// <summary>Saves one item; a failure (e.g. a locked file) is logged and only affects that item.</summary>
	private static async Task SaveSafely(IFoxyAutoSaverItem item)
	{
		try
		{
			await item.Save();
		}
		catch (Exception ex)
		{
			ErrorHandeler.FoxyLogger.AddError(ex);
		}
	}

	public void Dispose()
	{
		_queueTimer?.Dispose();
		foreach (var item in _items)
		{
			item.SaveRequested -= AddToQueue;
		}
		_items.Clear();
	}
} 