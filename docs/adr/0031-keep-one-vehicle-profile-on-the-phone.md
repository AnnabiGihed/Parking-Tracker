# ADR-0031: Keep one vehicle profile on the phone

- Status: Proposed
- Date: 2026-11-20
- Deciders: Gihed Annabi

## Context

The phone UI rework adds a "My vehicle" entry to the side menu. The owner wants the app to know the user's car so that a
later release can suggest where to charge or park, and can warn about the Brussels low-emission zone (LEZ). The LEZ
decides by fuel and Euro emission standard; charging suggestions need the plugs, the battery and the range. Today the
only vehicle fact the app holds is the plate on the residents' card.

ADR-0019 lists the aggregates of the driver assistant context (`ResidentCard`, `SavedPlaces`, `ParkingSession`).
ADR-0015 keeps the driver's data on the phone only, and ADR-0026 stores it with sqlite-net-pcl, with numbered schema
steps and the plate encrypted by the field cipher.

Forces:

- The owner chose one vehicle per phone, entered by hand, with no external vehicle service.
- A residents' card is issued for one plate, but the user may drive another car (a rental or a shared car) and still
  hold the card. The two plates are different facts.
- A plate is personal data, as on the card.
- Free text for make or fuel would make suggestions unreliable; a full make and model catalogue would need a source to
  keep current.

Options considered:

1. A curated set of fields on the phone: make from an in-app list, free model text, year, fuel, Euro standard, and the
   plugs, battery and range only for a car that plugs in.
1. Look the car up in an external vehicle catalogue or registration service by plate.
1. Reuse the plate of the residents' card as the vehicle's plate.

## Decision

We keep one vehicle profile on the phone with curated fields (option 1), as a fourth aggregate of the driver assistant
context, extending the list of ADR-0019.

- **Aggregate.** `Vehicle` (`LightweightAggregateRoot<VehicleId>`) holds the make code, model, model year, `FuelType`,
  `EuroNorm`, the set of `ChargingConnector` values, battery kWh, range km and an optional `LicencePlate`. Its rules: the
  make is a code of the in-app list `VehicleMakes` (with "Other" for any unlisted make); the model is at most 60
  characters; the year is from 1950 to the current year plus one, where the caller passes the current year so that the
  domain does not read the clock; plugs, battery (1 to 250 kWh) and range (10 to 1,000 km) exist only for an electric
  car or a plug-in hybrid. It is created, updated and restored through `Create`, `Update` and `Restore`, like the other
  phone aggregates, and raises no events. `Restore` does not check the latest year again: a phone whose clock went back
  must still read a profile that was valid when it was saved.
- **No external catalogue.** The make list ships with the app. A make code is never renamed or removed; a new make gets
  a new code. Nothing about the vehicle is sent to any service to fill or check the profile.
- **Plate kept apart from the card.** The vehicle has its own optional plate. `LicencePlate` moves to the shared value
  objects so the card and the vehicle use the same normalisation, but neither copies the other's value.
- **Storage.** Schema step 2 (`AddVehiclesStep`) adds the `vehicles` table. Enum values are stored as their numbers and
  are never renumbered; the plugs are a JSON array of numbers. The plate is encrypted by `FieldCipher` and bound to
  `vehicles.plate` and the profile's identifier, so a ciphertext copied from the card or another row is refused. A
  `slot` column that is always 1 and unique holds the database to one profile, as for the card. The encrypted-data
  probe counts a profile with a plate, so a lost key is never silently replaced while a plate is stored.
- **No server sync.** As ADR-0015 requires, the profile stays on the phone. A reinstall or a new phone asks for it again.

## Consequences

### Positive

- The LEZ and charging suggestions get reliable, typed inputs (fuel, Euro standard, plugs) instead of free text.
- The profile works offline and leaks nothing: no plate or car reaches a third party or the API.
- A rental car's plate never overwrites the plate on the residents' card, and the other way round.
- Schema step 2 is the first upgrade step of the device database, so the step mechanism of ADR-0026 is exercised on
  real data before more steps follow.

### Negative

- The make list must be maintained by hand in app releases; a new brand shows as "Other" until a release adds it.
- The user types the model, year and charging values; nothing checks them against the real car, and a typo stays.
- One profile only: a household with two cars must edit the profile when switching.
- The profile is lost with the phone or a reinstall, like the rest of the driver's data (ADR-0015).

## Alternatives considered

- **External vehicle catalogue or registration lookup (option 2).** Rejected: the owner chose no external vehicle
  service; a plate lookup would send personal data to a third party, needs a paid or national source with its own
  terms, and fails offline.
- **Reuse the card's plate (option 3).** Rejected: the card's plate and the car being driven can differ, and coupling
  them would make editing one silently change the other.
- **Several vehicles per phone.** Rejected for now: the owner chose one vehicle; a list can come later with a new schema
  step and an ADR that supersedes this one.
