# ADR-0003: Read parking-card photos with ML Kit text recognition on the device

- Status: Accepted; implemented in stage 6b, to be proven on a phone
- Date: 2026-11-16
- Deciders: Gihed Annabi

## Context

The card dialog can fill the card number, plate, municipality, sector, tolerance codes and validity dates from a photo.
Today `app.js` loads Tesseract.js 5.1.1 from `cdn.jsdelivr.net` on first use, downloads the French language data,
recognises the image in the WebView (the screen says it "can take ~20 s"), then `parseCardText` extracts the fields
with regular expressions.

In the MAUI app (ADR-0001) the page runs in a `BlazorWebView`. Options:

1. Keep Tesseract.js in the WebView, with an `<input type="file">` for the photo.
2. Take the photo with MAUI `MediaPicker` and recognise it on the device with Google ML Kit text recognition (Latin
   script) through the `Xamarin.Google.MLKit.TextRecognition` binding (116.0.1.9, `net10.0-android36.0`).
3. `Plugin.Maui.OCR` 1.1.1.

## Decision

We take or pick the photo with `MediaPicker` (`CapturePhotoAsync` / `PickPhotoAsync`) and recognise it with ML Kit's
bundled Latin text recogniser behind an `ICardTextReader` port (which takes or picks the photo and returns its text). `parseCardText` moves to
`ParkingTracker.Application` as `CardTextParser`, unit-tested with recorded OCR text, and keeps the same rules (number
pattern with `8`/`O` fixes, date range, plate, municipality and sector names, tolerance codes). The form is still
filled for the user to check; nothing is saved without "Save".

## Consequences

### Positive

- Recognition takes about a second, works offline and needs no third-party CDN at run time.
- The parsing rules become testable C# shared by every caller.
- The photo flow uses Android's own camera and picker, so it does not depend on file-input support in
  `BlazorWebView`.

### Negative

- The bundled model adds a few MB per ABI to the APK.
- ML Kit is a Google closed-source library; the binding version must follow .NET Android releases.
- Recognition quality differs from Tesseract's French model; the parser must be re-checked against real card photos
  (accents are not needed by the parser, digits and capitals are).
- CAMERA permission and MAUI's file provider must be declared; the old Capacitor `fileprovider` entry goes away.

## Alternatives considered

- **Keep Tesseract.js in the WebView** - rejected. It keeps a run-time download from a CDN, is slow on a phone, and
  depends on camera capture through a file input inside `BlazorWebView` on Android, which is unproven for this app.
- **`Plugin.Maui.OCR`** - rejected. Its latest package targets .NET 9 only and wraps the same ML Kit library with less
  control.
