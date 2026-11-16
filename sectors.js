// Residential parking sectors ("secteurs riverains") of the City of Brussels.
//
// Each street row is: [French name, Dutch name, odd numbers, even numbers]
// Source: official City of Brussels list "Secteur Louise"
// https://www.bruxelles.be/sites/default/files/bxl/LOUISE.pdf
// (linked from https://www.bruxelles.be/ou-stationner-avec-sa-carte-de-stationnement)
//
// Cell syntax (exactly as printed on the sheet):
//   ""            -> no number listed = the whole street side is covered
//   "hors"        -> "hors / buiten zone" = this side is NOT covered
//   "17-57b"      -> numbers 17 up to 57b (inclusive)
//   "59 à fin"    -> 59 up to the end of the street
//   "2-6 et 64-76"-> two ranges
//   "4"           -> only that number
//
// To add another sector, copy the "louise" block and fill in its sheet.
// `area` is a [south, west, north, east] box that must contain all the streets.

window.SECTORS = {
  louise: {
    id: 'louise',
    name: 'Louise',
    nameNl: 'Louiza',
    area: [50.808, 4.345, 50.845, 4.392],
    // The official documents this list was verified against (tools/verify_sectors.py).
    // At every start the app checks them live; if the City changes a document, the app
    // stops giving answers based on this list until the list is re-verified and updated.
    sources: [
      {
        url: 'https://www.bruxelles.be/sites/default/files/bxl/LOUISE.pdf',
        etag: '"6989b7d0-9f3b"', length: 40763, lastModified: 'Mon, 09 Feb 2026 10:32:48 GMT',
        sha256: '0205dfe7ce97bb814b6b98334d1c590bb3bbcd6922d5d637001060d0a045a9d7',
      },
      {
        url: 'https://www.bruxelles.be/sites/default/files/bxl/accord%20limitrophe%20Louise%20-%20Ixelles.pdf',
        etag: '"6a9ea611-361c5"', length: 221637, lastModified: 'Mon, 07 Sep 2026 11:54:57 GMT',
        sha256: 'fd6ffc486d37617c84721a251cdd686088577851e0225ad65743e308cf314e08',
      },
    ],
    streets: [
      ['Allée du Cloître', 'Kloosterdreef', '', ''],
      ['Avenue du Congo', 'Kongolaan', '7-15', 'hors'],
      ['Avenue Emile De Mot', 'Emile De Motlaan', '', ''],
      ['Avenue Emile Duray', 'Emile Duraylaan', 'hors', '64-68'],
      ['Avenue Franklin Roosevelt', 'Franklin Rooseveltlaan', '1-11', '2-10'],
      ['Avenue Legrand', 'Legrandlaan', '1-25', '2-22'],
      ['Avenue Lloyd George', 'Lloyd Georgelaan', '', ''],
      ['Avenue Louise', 'Louizalaan', '59 à fin', '54 à fin'],
      ['Boulevard de la Cambre', 'Terkamerenlaan', '17-57b', '6-74'],
      ['Chaussée de Vleurgat', 'Vleurgatsesteenweg', '107-149', '106-218'],
      ['Place Stéphanie', 'Stefaniaplein', '1A-3', 'hors'],
      ['Rue Blanche', 'Blanchestraat', '1-13', '2-12'],
      ['Rue Dautzenberg', 'Dautzenbergstraat', '63-65', '68-84'],
      ['Rue de Belle Vue', 'Bellevuestraat', '3-25', '24-64'],
      ['Rue De Crayer', 'De Crayerstraat', '', ''],
      ['Rue de Florence', 'Florencestraat', '1-13', '2-6'],
      ['Rue de la Bonté', 'Goedheidsstraat', '1-5', '2-4A'],
      ['Rue de la Concorde', 'Eendrachtsstraat', '59-67', '68A-70'],
      ['Rue de la Grosse Tour', 'Wollendriestoren', '1-25', 'hors'],
      ['Rue de la Longue Haie', 'Langehaagstraat', '3-67', '2-6 et 64-76'],
      ['Rue de la Vallée', 'Dalstraat', '31-67', '44 à fin'],
      ['Rue de la Vanne', 'Verlaatstraat', '1-47', '4'],
      ["Rue de l'Abbaye", 'Abdijstraat', '1-11', '2-4'],
      ["Rue de l'Aurore", 'Dageraadstraat', '', ''],
      ['Rue de Livourne', 'Livornostraat', '125-129', '12-160'],
      ['Rue De Praetere', 'De Praeterestraat', 'hors', '2-4'],
      ['Rue Defacqz', 'Defacqzstraat', '1', '2-24'],
      ['Rue du Bailli', 'Baljuwstraat', '1-31', '2-36'],
      ['Rue du Beau Site', 'Welgelegenstraat', '', ''],
      ['Rue du Buisson', 'Braambosstraat', 'hors', '2'],
      ['Rue du Châtelain', 'Kasteleinsstraat', '1-19', '2-8B'],
      ['Rue du Lac', 'Meerstraat', '35A à fin', '22-54'],
      ['Rue du Monastère', 'Munsterstraat', '5 à fin', '20-46'],
      ['Rue du Président', 'Voorzittersstraat', '71-75', 'hors'],
      ['Rue Emile Claus', 'Emile Clausstraat', '1-11', '2-12'],
      ['Rue Gachard', 'Gachardstraat', '67-79', '88-104'],
      ['Rue Jacques Jordaens', 'Jakob Jordaensstraat', '', ''],
      ['Rue Kindermans', 'Kindermansstraat', 'hors', '2-22'],
      ['Rue Lens', 'Lensstraat', '11-33', 'hors'],
      ['Rue Lesbroussart', 'Lesbroussartstraat', '123', '128'],
      ['Rue Mercelis', 'Mercelisstraat', '89-103', '70-90'],
      ['Rue Paul Emile Janson', 'Paul Emile Jansonstraat', '1-3A', '2-14'],
      ['Rue Paul Lauters', 'Paul Lautersstraat', '1-11', '2-2A'],
      ['Rue Paul Spaak', 'Paul Spaakstraat', '1-35', 'hors'],
      ['Rue Souveraine', 'Opperstraat', '107-115', '110-126'],
      ['Rue Van Eyck', 'Van Eyckstraat', 'hors', '8-54'],
      ['Rue Vilain XIIII', 'Vilain XIIII-straat', '19-55', '22-46'],
      ['Rue Washington', 'Washingtonstraat', 'hors', '2-6'],
      ['Tenbosch', 'Tenbos', '3-21', '2-34'],
    ],

    // Tolerance agreements with neighbouring municipalities. They only apply if
    // the card carries the code. Here an empty cell or "hors" means NOT covered.
    tolerances: [
      {
        code: 'BXL_LIM_IXL_LOUISE',
        label: 'Ixelles – Louise tolerance',
        commune: 'Ixelles', // applies only on Ixelles territory (municipality given live by parking.brussels)
        // Source: https://www.bruxelles.be/sites/default/files/bxl/accord%20limitrophe%20Louise%20-%20Ixelles.pdf
        // (streets on Ixelles / 1050 territory open to City of Brussels residents)
        streets: [
          ['Rue de la Grosse Tour', 'Wollendriestoren', 'hors', '2-24'],
          ["Rue de l'Abbaye", 'Abdijstraat', '15-65', '6A-30'],
          ['Rue du Bailli', 'Baljuwstraat', '19A-51', '36-54'],
          ['Rue de Belle Vue', 'Bellevuestraat', 'hors', '2-24'],
          ['Rue du Buisson', 'Braambosstraat', 'hors', '4-12'],
          ['Rue de la Vallée', 'Dalstraat', '1-29', '2-42'],
          ['Rue Dautzenberg', 'Dautzenbergstraat', '1-61', '2-66'],
          ['Rue Defacqz', 'Defacqzstraat', '3-23', '14-44'],
          ['Rue de la Concorde', 'Eendrachtsstraat', '37-59', '42-66'],
          ['Rue Emile Claus', 'Emile Clausstraat', '13-43', '14-42'],
          ['Rue de Florence', 'Florencestraat', '15-41', '8-46'],
          ['Rue Gachard', 'Gachardstraat', '1-65', '2-86'],
          ["Rue de l'Arbre Bénit", 'Gewijdeboomstraat', '105-129', '118-120'],
          ['Rue du Châtelain', 'Kasteleinsstraat', '25-69', '10-56'],
          ['Rue Kindermans', 'Kindermansstraat', 'hors', '2 à fin'],
          ['Rue du Couvent', 'Kloosterstraat', '1 à fin', '2 à fin'],
          ['Avenue du Congo', 'Kongolaan', '1-5', '2-6'],
          ['Rue de la Croix', 'Kruisstraat', '47-65', '46-74'],
          ['Rue de la Longue Haie', 'Langehaagstraat', 'hors', '8-62'],
          ['Avenue Legrand', 'Legrandlaan', '27-67', '22-82'],
          ['Rue Lens', 'Lensstraat', 'hors', '2 à fin'],
          ['Rue Lesbroussart', 'Lesbroussartstraat', '95-119', '102-124'],
          ['Rue de Livourne', 'Livornostraat', '25-119 et 131-147', 'hors'],
          ['Rue du Lac', 'Meerstraat', '5-35', '2-20'],
          ['Rue Mercelis', 'Mercelisstraat', '57-87', '48-68'],
          ['Rue Souveraine', 'Opperstraat', '53-105', '52-108'],
          ['Rue Paul Emile Janson', 'Paul Emile Jansonstraat', '5-39', '16-52'],
          ['Rue Paul Lauters', 'Paul Lautersstraat', '13-41', '4-32'],
          ['Rue Paul Spaak', 'Paul Spaakstraat', 'hors', '2 à fin'],
          ['Place Stéphanie', 'Stefaniaplein', 'hors', '2-20'],
          ['Boulevard de la Cambre', 'Terkamerenlaan', '1-3', '2-12'],
          ['Tenbosch', 'Tenbos', '23-73', '40-58'],
          ['Rue Van Eyck', 'Van Eyckstraat', '1-61', 'hors'],
          ['Rue de la Vanne', 'Verlaatstraat', 'hors', '2'],
          ['Rue Vilain XIIII', 'Vilain XIIII-straat', '1-17', '2-20'],
          ['Rue du Président', 'Voorzittersstraat', '31-69', '40-76'],
          ['Rue Washington', 'Washingtonstraat', '1-51', '8-68'],
          ['Rue du Magistrat', 'Wethoudersstraat', '9-53', '24-54'],
          ['Chaussée de Vleurgat', 'Vleurgatsesteenweg', '57-95', '58-104'],
        ],
      },
    ],
  },
};
