// The 19 municipalities of the Brussels-Capital Region and how their residents' card works.
//
// kind:
//   'streets' — sectors defined by street + house-number lists (City of Brussels, see city-sectors.js)
//   'mailles' — the card lists one or more "mailles" (areas). Their exact shapes are published live by
//               parking.brussels (layer permit_secteurs_mailles); `prefixes` = their zone codes.
//   'whole'   — no sectors: the card is valid in the municipality's card zones everywhere.
//   'wsl'     — Woluwe-Saint-Lambert: blue zones in the whole municipality; green/grey zones only in the
//               neighbourhood printed on the card, whose boundaries are not published.
//   'unknown' — sectors exist but their boundaries are not published: the app asks you to check your card.
// In every case, whether a zone accepts residents' cards at all comes live from the zone rules
// (parking.brussels / Brussels Mobility: DEROGATION, DISK and residents-only zones accept it; PAYABLE never).
// `mu` is the municipality name exactly as parking.brussels writes it.
//
// Sources: bruxelles.be (sector lists), parking.brussels (mailles, zone rules), woluwe1200.be, uccle.be,
// etterbeek.brussels (checked 16/11/2026).

window.MUNICIPALITIES = [
  { id: 'bruxelles', name: 'Bruxelles (Ville)', nameNl: 'Brussel (Stad)', mu: 'BRUXELLES', kind: 'streets' },
  { id: 'anderlecht', name: 'Anderlecht', nameNl: 'Anderlecht', mu: 'ANDERLECHT', kind: 'mailles', prefixes: ['ANDR'] },
  { id: 'auderghem', name: 'Auderghem', nameNl: 'Oudergem', mu: 'AUDERGHEM', kind: 'mailles', prefixes: ['AUDR', 'OUDR'], lim: ['LIM_AUD_WABO'] },
  { id: 'berchem', name: 'Berchem-Sainte-Agathe', nameNl: 'Sint-Agatha-Berchem', mu: 'BERCHEM-SAINTE-AGATHE', kind: 'mailles', prefixes: ['BSAR'] },
  { id: 'etterbeek', name: 'Etterbeek', nameNl: 'Etterbeek', mu: 'ETTERBEEK', kind: 'whole',
    note: 'Etterbeek has no sectors: the residents\' card is valid in the green zones of the whole municipality. Border streets with neighbouring municipalities need a specific card.' },
  { id: 'evere', name: 'Evere', nameNl: 'Evere', mu: 'EVERE', kind: 'mailles', prefixes: ['EVER'] },
  { id: 'forest', name: 'Forest', nameNl: 'Vorst', mu: 'FOREST', kind: 'mailles', prefixes: ['FORR'] },
  { id: 'ganshoren', name: 'Ganshoren', nameNl: 'Ganshoren', mu: 'GANSHOREN', kind: 'mailles', prefixes: ['GSHR'] },
  { id: 'ixelles', name: 'Ixelles', nameNl: 'Elsene', mu: 'IXELLES', kind: 'mailles', prefixes: ['IXLR'], lim: ['LIM_IXL_BRU_ROOSEVELT'] },
  { id: 'jette', name: 'Jette', nameNl: 'Jette', mu: 'JETTE', kind: 'mailles', prefixes: ['JETR'] },
  { id: 'koekelberg', name: 'Koekelberg', nameNl: 'Koekelberg', mu: 'KOEKELBERG', kind: 'mailles', prefixes: ['KOEKR'] },
  { id: 'molenbeek', name: 'Molenbeek-Saint-Jean', nameNl: 'Sint-Jans-Molenbeek', mu: 'MOLENBEEK-SAINT-JEAN', kind: 'mailles', prefixes: ['SJMR'] },
  { id: 'saint-gilles', name: 'Saint-Gilles', nameNl: 'Sint-Gillis', mu: 'SAINT-GILLES', kind: 'mailles', prefixes: ['STGR'], lim: ['LIM_STG_BRU_POINCARE'] },
  { id: 'saint-josse', name: 'Saint-Josse-ten-Noode', nameNl: 'Sint-Joost-ten-Node', mu: 'SAINT-JOSSE-TEN-NOODE', kind: 'unknown',
    note: 'Saint-Josse does not publish where its residents\' card is valid, so the app can tell you the zone rules but not whether your card covers the spot.' },
  { id: 'schaerbeek', name: 'Schaerbeek', nameNl: 'Schaarbeek', mu: 'SCHAERBEEK', kind: 'mailles', prefixes: ['SCHR'] },
  { id: 'uccle', name: 'Uccle', nameNl: 'Ukkel', mu: 'UCCLE', kind: 'unknown',
    note: 'Uccle\'s card is valid only in the green and blue zones of the sector assigned to your address, but Uccle does not publish the sector boundaries. The app tells you the zone rules; check your sector on your card.' },
  { id: 'watermael', name: 'Watermael-Boitsfort', nameNl: 'Watermaal-Bosvoorde', mu: 'WATERMAEL-BOITSFORT', kind: 'mailles', prefixes: ['WABOR'], lim: ['LIM_AUD_WABO'] },
  { id: 'woluwe-lambert', name: 'Woluwe-Saint-Lambert', nameNl: 'Sint-Lambrechts-Woluwe', mu: 'WOLUWE-SAINT-LAMBERT', kind: 'wsl',
    note: 'Woluwe-Saint-Lambert\'s card is valid in all blue zones of the municipality, but in green and grey zones only in the neighbourhood printed on your card, whose boundaries are not published.' },
  { id: 'woluwe-pierre', name: 'Woluwe-Saint-Pierre', nameNl: 'Sint-Pieters-Woluwe', mu: 'WOLUWE-SAINT-PIERRE', kind: 'mailles', prefixes: ['WSPP', 'WSPR'] },
];
