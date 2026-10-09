# Private instances — ask the user first

**Before changing anything about InPrivate / private instances, ask the user.** The design is theirs and
still open; don't decide these on your own. How it works today: `Docs/architecture/web-ui.md` → *Private
instances*.

Settled: private is a property of an instance, never of a window (browser → instances → windows → tabs). There
are no private windows, no "New private window" or "Open link in private window".

## Open questions to bring up

- [ ] **Extensions.** The guard that skipped extension setup for InPrivate tabs was removed, untested. Do
      extensions install from the store and run in a private instance's tabs? If not, that is a bug the user
      wants to find and fix.
- [ ] **Extension popup profile.** `ExtensionPopupWebview` (`MainWindow.xaml.cs`) is created without the InPrivate
      option, so in a private instance popups run in the instance's on-disk WebView2 profile while its tabs run
      in the in-memory InPrivate twin. Popups don't see the tabs' cookies or sign-ins.
- [ ] **What else "private" should mean.** Today the setting only flips `IsInPrivateModeEnabled`. FoxyBrowser's
      own history, downloads list, remembered permissions and session restore still follow their own settings,
      so a private instance saves history and restores its tabs unless those are turned off.
- [ ] **When the setting applies.** `Instance.IsPrivate` is read once at startup, so changing it needs a restart.
      Fine, or should it reopen the instance's windows?
