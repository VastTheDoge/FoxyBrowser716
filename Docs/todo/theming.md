# Theming — check in the running app

The theme gallery/editor, color picker and the reworked settings page (2026-10-26) were built and compile but
have not been run. See `Docs/architecture/theming.md`.

- [ ] Settings page: nav buttons with icons, the one in view highlighted while scrolling; category cards with
      separators; the Permissions subheading; the page gradient.
- [ ] Search: results replace the nav while typing (ranked; typos like "downlod" still find Download folder);
      clicking one scrolls to the setting and flashes it; clearing the box brings the categories back.
- [ ] Gallery: previews show this window's home picture; clicking a card applies the theme; the in-use card is
      outlined and says "In use".
- [ ] Editor: swatch opens the picker in a flyout that follows the theme; dragging the square and hue bar, typing
      hex and RGB all stay in step; the preview updates but the window does not; Save then Use applies; Cancel
      drops the draft.
- [ ] Custom themes: edit, copy, delete (two-click), delete while in use falls back to Dark Mode; names made unique.
- [ ] Import/Export round trip through a `.foxytheme` file; importing a non-theme file shows the error toast.
- [ ] Card sizing in `VariableSizedWrapGrid`: nothing clipped at the bottom of a card, buttons fit on one row.
- [ ] A second instance sees a theme saved in the first (the list is global) and recolors if it uses that theme.

Follow-ups:

- [ ] Home page background picker could move into Appearance too (it is still in the home page's own menu).
- [ ] The editor swatches could show a tooltip with the hex.
