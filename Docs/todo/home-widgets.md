# Home widgets

All widgets compile; none have been looked at in the running app yet.

- [ ] Add each widget and resize it tiny / wide / tall — nothing clips or jitters: Search, Analog Clock,
      Calendar, Bookmarks, Quick Link, Sticky Note, Pomodoro, Stopwatch, Countdown, Browser Stats, Greeting,
      Speed Test, Web Page, Weather, World Clock, Photo Frame, News Feed, Top Sites, Checklist, Now Playing,
      System Monitor.
- [ ] Widget settings: open from the edit overlay, change values, Save and Exit, restart — values persist.
- [ ] Sticky note and checklist edits persist after a restart (they save outside edit mode).
- [ ] Theme switch recolors every widget.
- [ ] Speed Test: live graph moves while downloading; a test reports sensible ping/download/upload; the
      stop button cancels mid-test.
- [ ] Web Page: page loads signed in (shared profile); "Open Links In New Tab" sends clicks to tabs; it keeps
      running (and playing audio) while a tab hides the home page — consider `CoreWebView2.TrySuspendAsync`
      when the home page is collapsed.
- [ ] Photo Frame: if pictures appear ~10s late, its ImageOpened events aren't firing on the hidden layer and
      the 10s fallback in `Tick` is doing the reveal.
- [ ] World Clock: if the time column is clipped, `TextBlock.Measure` outside the visual tree returned 0 and
      the 0.55em-per-character fallback is too small for that culture.
- [ ] Now Playing: album art shows (the stream is disposed right after `SetSourceAsync`).
- [ ] `DateTimeWidget`: its Viewbox content width follows the time string, so it rescales slightly as
      the time changes — give it a fixed design width like the newer widgets.
- [ ] Widget settings popup: the background-image folder picker closes the popup (existing TODO in
      `HomePage.OptionClicked`); check whether Photo Frame's file/folder pickers have the same issue.
