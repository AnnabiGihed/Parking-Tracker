# Parking Tracker 🅿️

A small web app for Brussels residential parking cards (*carte riverain / bewonerskaart*).
It tells you whether you can park **for free with your card** where you are now, or where you drop a pin,
and sends you to the nearest covered street in **Waze** or **Google Maps**.

## Features

- **My card** (🪪): type your card details or **scan a photo** of the card (text recognition fills in the number, plate, sector and validity dates).
  Data stays in your browser only (localStorage). It warns you 30 days before the card expires, with a link to e-mail the renewal.
- **Live position** (📍): follows your GPS and shows **FREE ✓** / **PAY** as you drive. Your phone vibrates when the status changes.
- **Pin**: tap anywhere on the map (or search an address) to check that spot. You can drag the pin.
- **Nearest free spot** (🟢): the closest covered address, with Waze / Google Maps buttons.
- Green dots on the map are the covered addresses in your sector.

## Tolerance zones

The City of Brussels has agreements with neighbouring municipalities. For the Louise sector, the
[Ixelles – Louise agreement](https://www.bruxelles.be/sites/default/files/bxl/accord%20limitrophe%20Louise%20-%20Ixelles.pdf)
opens about 40 extra Ixelles streets, but **only if your card carries the code `BXL_LIM_IXL_LOUISE`**.
Tick it in the card dialog if your card has it. Ask the City if you are not sure.

## Where the information comes from

| What | Source | When |
|---|---|---|
| Which street surface you are on, its municipality and zone colour (red = residents' cards never valid, grey/green/orange = card valid in your sector, unregulated, blue = disc…) | **parking.brussels API** (`regulation_zones`, WFS) | **live, at every check** |
| Which streets and house numbers your card covers | City of Brussels official sector list (`sectors.js`) — no API publishes City of Brussels sectors | checked against the official PDFs with `tools/verify_sectors.py` |
| Which house number you are in front of | OpenStreetMap house-number positions (Overpass API) | downloaded at every app start |
| Where you are | phone GPS, high accuracy, no cached fixes | continuously; re-checked every 8 m or 30 s |

The app never answers from old data: no internet, no answer from parking.brussels, GPS worse than ±25 m or a
fix older than 15 s gives **CAN'T VERIFY** instead of FREE/PAY.

Note: parking.brussels does not publish the hours/prices table through its API, so the app does not know whether
paid parking is suspended at night or on Sundays; it answers as if the rules apply.

## How it decides

1. `sectors.js` holds the street list from the paper sheet (street, odd numbers, even numbers).
   An empty cell means the whole side is covered; `hors` means that side is not covered.
2. On first launch, the app loads street shapes and house numbers for the sector area from OpenStreetMap (Overpass API)
   and caches them on the device for 30 days.
3. For a position it finds the nearest street. If the street is on the list, it uses the nearest house number to know the side
   (odd or even) and the number, then checks the ranges.
4. If covered and not-covered numbers are both within GPS accuracy (about 12 m) of the spot, it shows
   **CHECK THE NUMBER** instead of guessing.

Always check the street signs: works, delivery zones, disabled spots and other local rules still apply.

## Run locally

```bash
node tools/dev-server.mjs 8766
```

Then open http://localhost:8766. The dev server forwards `/pbapi/*` to the parking.brussels API, because that API
does not allow direct calls from web pages (the Android app calls it natively).

## Android app

Every push to `main` builds a signed APK on GitHub Actions (`.github/workflows/android.yml`) and publishes it on the
repository's **Releases** page.

**Install on the phone**
1. On the phone, open `https://github.com/AnnabiGihed/Parking-Tracker/releases/latest` and tap **ParkingTracker.apk**.
2. Open the downloaded file. Android asks to allow installing from Chrome ("install unknown apps"): allow it, then **Install**.
3. Open **Parking Tracker**, add your card (🪪), and allow location when asked.

**Updating:** install the newer APK from the same page; it replaces the old one and keeps your card.

**Signing key:** the key is in `signing/` on the PC that created it (not in git) and in the repository secrets
`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`.
Back up `signing/`: without it, a new APK can't update the installed app (you'd have to uninstall it first).

**Local build** (needs Android Studio / Android SDK + JDK 21): `npm ci && npm run sync`, then open `android/` in Android Studio.

## Check the street data

```bash
python tools/verify_sectors.py
```

Downloads the official City of Brussels PDFs and compares every street and number range with `sectors.js`
(needs `pdftotext`, which comes with Git for Windows). It prints `ALL MATCH ✓` when everything is identical.

## Web version

The same app also runs in a browser. GPS in the browser needs HTTPS, for example GitHub Pages
(**Settings → Pages → Deploy from branch → main / root**).

## Add another sector

Copy the `louise` block in `sectors.js`, fill in the streets from your sheet and set `area` to a box that contains them all.
