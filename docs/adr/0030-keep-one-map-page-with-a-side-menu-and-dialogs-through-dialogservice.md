# ADR-0030: Keep one map page with a side menu, map-mode pickers and dialogs through DialogService

- Status: Accepted
- Date: 2026-11-20
- Deciders: Gihed Annabi

## Context

ADR-0020 chose Radzen.Blazor for the phone screens and kept Leaflet for the map. It also said that dialogs go
through Radzen's `DialogService`. Today the app has one route (`/`) and four dialogs of its own (`DialogShell`:
welcome, card, car, place). The owner wants a side menu with Home, Work, the parking card and, in later releases,
favourite places and a vehicle profile, and wants the phone's back button to close what is open.

- The Leaflet map is one JavaScript object, created when `MapView` renders and destroyed in `MapView.DisposeAsync`.
  A second route would destroy the map and its layers when the user leaves the page, and build them again when they
  come back.
- Radzen's `DialogService` draws dialogs above the page in a host the layout already holds (`RadzenComponents`). Its
  side dialog (`OpenSide`) slides in from an edge over a mask.
- Radzen's own dialog mask and dialog use z-index 1000 and 1001 by default. The search bar is at 1000, the layers
  popover at 1100, the app's own dialog backdrop at 1500, Radzen's popups (calendar, drop-down list) at 2000, the toast
  at 2000. With the defaults, a Radzen dialog would sit under the search bar.
- The view models have no reference to Radzen (ADR-0020). A view model owns `IsOpen`; a component draws it.
- The phone's back button leaves the app unless the host handles it.

## Decision

1. **One route.** The app keeps the single page `/`. New screens are dialogs or a mode of the map page (for example an
   address picker that shows a pin on the map). No new route is added while the map is a singleton.
2. **The menu is a Radzen side dialog.** The menu button `#menuBtn` in the search bar calls
   `DialogService.OpenSide<AppMenu>` (left, mask on, a tap on the mask closes it, 300 px wide). `AppMenu` is a
   `RadzenPanelMenu`; its texts come from `MenuViewModel`, which reads `PlacesViewModel` and `CardViewModel`. A tap on an
   entry closes the menu and then opens the existing screen, so a Radzen dialog is never open over one of the app's own
   dialogs.
3. **`DialogBridge` joins a view model to `DialogService`.** The component in `Shared/Features/Shared/Dialogs` watches
   `IDialogViewModel.IsOpen` and calls `DialogService.OpenAsync` or `Close`; when the user closes the dialog
   (`DialogService.OnClose`), it calls the view model's `Close`. View models stay free of Radzen. New dialogs use it from
   now on; the existing four move to it in a later change, and `DialogShell` is deleted then.
4. **Layers.** `radzen-theme.css` sets `--rz-dialog-mask-zindex: 1500` and `--rz-dialog-zindex: 1501`; the toast is
   raised to 2100 so that it stays above the Radzen popups (2000); the map stays at z-index 0. A test reads the three
   values from the shipped stylesheets.
5. **Back button.** `IBackNavigation` (ViewModels) is a stack of close actions. The menu, the bridge and every
   `DialogShell` that is given `OnBack` register while they are open. `MainPage.OnBackButtonPressed` asks the singleton
   whether something is open; if so it dispatches `TryGoBack` into the Blazor scope with
   `BlazorWebView.TryDispatchAsync` and tells MAUI the press was handled. Otherwise the default behaviour runs and the
   app leaves.

## Consequences

### Positive

- The map and its layers are built once and never destroyed by navigation.
- The menu is a standard Radzen component, with Radzen's mask, focus handling and animation, and costs little code.
- The back button works the same for the menu and for every dialog, and the stack is unit-tested without a phone.
- View models stay testable without Radzen.

### Negative

- Everything that is not a dialog or a map mode needs a design: a full page would need the map to survive navigation.
- `DialogShell` and `DialogBridge` live side by side until the existing dialogs are moved.
- Radzen's panel menu puts its click callback on the list item; bUnit tests tap the item, not the link inside it.
- The back button path (MAUI to Blazor) can only be proved on a phone or an emulator, not in a unit test.

## Alternatives considered

- **`RadzenLayout` with `RadzenSidebar` and routed pages** - rejected: they are page-layout parts (z-index 1, no mask,
  they push the content), and routing away from `/` destroys the map.
- **A second route per screen, with the map kept alive by a service** - rejected: it needs a map object that outlives
  its component and a way to re-attach it; the gain is only a back stack the dialog stack already gives.
- **A hand-written side panel** - rejected: ADR-0020 chose Radzen so that the screens stop being hand-written HTML.
- **Handling the back button in the activity (`OnBackPressedDispatcher`)** - rejected: the page is the only owner of the
  Blazor scope, and `MainPage` keeps the host code small and the same on every platform.

## References

- [ADR-0020](0020-use-radzen-for-the-phone-screens-and-keep-leaflet-for-the-map.md): Radzen for the phone screens;
  this decision follows it and does not change it.
- Architecture, section 3.12.
