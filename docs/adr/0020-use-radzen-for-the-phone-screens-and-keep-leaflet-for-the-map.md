# ADR-0020: Use Radzen.Blazor for the phone screens and keep Leaflet for the map

- Status: Proposed
- Date: 2026-11-17
- Deciders: Gihed Annabi

## Context

ADR-0001 kept the hand-written HTML and CSS of the Capacitor app inside the MAUI `BlazorWebView`, with Leaflet behind
`map.js`. The owner now asks for the mobile screens to move to Radzen components, the same library as the admin app
(ADR-0017). The map is the product: it draws the parking.brussels WMS zone layer and styled polygons. The emulator test
reads `.hero-title`, `.hero-sub` and `#welcomeDialog`. The bUnit tests run JS interop in strict mode.

Options considered:

1. Radzen for dialogs, forms and lists, Leaflet kept for the map.
1. Radzen everywhere, including its map component.
1. Keep the hand-written HTML and CSS.

## Decision

We add Radzen.Blazor to `ParkingTracker.Shared` and keep Leaflet for the map (option 1).

- Dialogs through `DialogService` and `RadzenDialog`, notifications through `RadzenNotification`, and `RadzenTextBox`,
  `RadzenDatePicker`, `RadzenDropDown` (multiple selection for mailles), `RadzenSwitch`, `RadzenButton` and
  `RadzenCard` for forms, switches, buttons and the status sheet; new screens (sign-in, places, account) use the same.
- The map stays Leaflet in `wwwroot/js/map.js` behind the existing interop module.
- One theme file sets Radzen's CSS variables to the current colours.
- The selectors the emulator test reads are kept on the Radzen markup.
- bUnit tests set up Radzen's JS interop calls explicitly; `map.js` interop stays strict.
- The move is done one dialog per pull request (stage S2).

## Consequences

### Positive

- One component library for the phone and the admin app; less hand-written CSS and markup to maintain.
- Accessible, consistent form controls for the new screens.

### Negative

- The APK grows by the Radzen assembly and its assets.
- The look of the app changes for existing users.
- bUnit tests need Radzen's interop set up, which adds test plumbing.

## Alternatives considered

- **Radzen's map component** - rejected. It is based on Google Maps and cannot draw the parking.brussels WMS layer and
  the polygon styling.
- **Keep the hand-written HTML and CSS** - rejected. It contradicts the owner's requirement.
