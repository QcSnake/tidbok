# Tidbok

Onlinebokning för småföretag. Kunden väljer tjänst, person och tid på verksamhetens hemsida.
Personalen ser dagen i en vy, blockerar tid och ändrar tjänster och arbetstider själva.

Testa: **https://tidbok.onrender.com** · två påhittade verksamheter, inga konton behövs.

Byggt i C# och .NET 8 av [Abdimalik Hassan](https://abdimalik-portfolio.onrender.com/).

## Vad det gör

**För kunden**

- Välj tjänst, person eller "första lediga", dag och tid. Dagar utan tider är utgråade med skälet
  (stängt, fullbokat, juldagen).
- Bekräftelse med bokningsnummer, kalenderfil (.ics) och en egen länk för att se eller avboka.
- Avboka själv fram till en gräns verksamheten väljer. Efter det hänvisas kunden till telefonen.

**För verksamheten**

- Dagsvy med en kolumn per person, bokningar och blockeringar på en tidslinje, veckans beläggning överst.
- Blockera tid (lunch, möte, sjukdom), avboka åt kunden, sök på namn, telefon eller bokningsnummer.
- Ändra tjänster, priser och tider, vem som gör vad, och arbetstider med upp till två pass per dag.
- Regler: hur tätt starttiderna ligger, hur sent man får boka och avboka, hur långt fram.
- En direktlänk att dela och två rader kod för att lägga bokningen på den egna hemsidan.

## Det som brukar gå fel, och hur det är löst

**Dubbelbokning.** Två kunder som klickar på samma tid samtidigt ska inte båda få den. Bokningen
sparas inne i `BEGIN IMMEDIATE`, som tar SQLite:s skrivlås direkt. Inne i låset läses dagens schema
på nytt och tiden kontrolleras en gång till innan raden skrivs. Testet
`Twenty_customers_clicking_the_same_time_at_once_give_exactly_one_booking` skickar tjugo samtidiga
bokningar på samma tid och kräver att exakt en lyckas.

**Röda dagar.** Påsk, Kristi himmelsfärd, midsommar och alla helgons dag flyttar sig varje år. De
räknas fram av [Helgdagar](https://github.com/QcSnake/helgdagar), ett litet bibliotek jag skrev för
det här. Ingen behöver lägga in juldagen för hand.

**Personuppgifter.** Namn, telefon, e-post och meddelande gallras automatiskt 30 dagar efter besöket.
Bokningen står kvar utan koppling till personen, så statistiken stämmer. Avbokningslänken sparas
bara som SHA-256-hash, så den som kommer åt databasen kan ändå inte avboka någon annans tid.

**Tidszoner.** Allt är verksamhetens lokala tid, eftersom öppettider är väggklockstider. Klockan går
via `TimeProvider`, så testerna kan ställa den exakt. Kalenderfilen skrivs i UTC och visar rätt
både sommar- och vintertid.

## Arkitektur

```
src/Tidbok.Core    Domänen. Lediga tider, regler, bokningstjänst, kalenderfil. Ingen databas, ingen webb.
src/Tidbok.Data    SQLite: schema, lagring, demodata.
src/Tidbok.Web     Minimal API för bokningen, Razor Pages för admin, statiska sidor och widget.
tests/             xUnit: reglerna, kalenderfilen och hela flödet mot en riktig databasfil.
```

`Availability` är rena funktioner: schema, upptagen tid och klockslag in, lediga tider ut. Samma
funktion används för att visa tider och för kontrollen inne i skrivlåset, så de kan inte säga olika.

**SQLite utan ORM.** Miljön projektet byggdes i nådde inte NuGet, så `Microsoft.Data.Sqlite` gick
inte att hämta. `Sqlite.cs` är ett tunt lager direkt mot SQLite:s C-API (open, prepare, bind, step)
som laddar systemets egen SQLite: `libsqlite3` på Linux och `winsqlite3.dll` som följer med
Windows. All SQL går genom parametrar. I ett team hade jag tagit EF Core, och lagringen ligger bakom
`ITidbokStore`, så bytet är en klass.

## API

| Metod | Sökväg | |
|---|---|---|
| GET | `/api/businesses/{slug}` | Verksamheten, tjänster och personal |
| GET | `/api/businesses/{slug}/days?service=&staff=&from=&count=` | Lediga dagar |
| GET | `/api/businesses/{slug}/slots?service=&staff=&date=` | Lediga tider en dag |
| POST | `/api/businesses/{slug}/bookings` | Boka. 201, 400 med fältfel eller 409 om tiden hann tas |
| GET | `/api/bookings/{token}` | Bokningen bakom en avbokningslänk |
| POST | `/api/bookings/{token}/cancel` | Avboka |
| GET | `/api/bookings/{token}/calendar.ics` | Kalenderfil |

Exempel att köra finns i `api.http`.

## På en hemsida

```html
<script src="https://tidbok.onrender.com/widget.js" data-tidbok="salong-silhuett" defer></script>
<button type="button" data-tidbok-open>Boka tid</button>
```

Knappen öppnar bokningen i en ruta ovanpå sidan. Bokningssidan är den enda som får bäddas in
(`frame-ancestors *`). Admin och avbokning går aldrig att rama in.

## Säkerhet

- Strikt CSP utan inline-skript och utan inline-stilar. Verksamhetens färg kommer som en egen liten
  stilmall, positionerna i dagsvyn sätts via CSSOM.
- Lösenord med PBKDF2-SHA256, 210 000 iterationer, unikt salt, jämförelse i konstant tid.
- Inloggningen bromsas efter åtta felaktiga försök per IP på en kvart. Bokning och avbokning är
  begränsade till tio anrop i minuten per IP.
- Admin hämtar alltid verksamheten från inloggningen, aldrig från adressen. Testerna kontrollerar
  att en salong inte kan blockera tid eller ändra arbetstider hos kliniken.
- Antiforgery på alla formulär i admin, `no-store` på sidor med personuppgifter.

## Köra lokalt

Kräver .NET 8 SDK. På Windows används `winsqlite3.dll` som redan finns i systemet.

```powershell
dotnet run --project src/Tidbok.Web --urls http://localhost:5290
```

Öppna http://localhost:5290. Databasen skapas i `src/Tidbok.Web/App_Data` med demodata.

## Tester

```powershell
dotnet test
```

49 tester. Lediga tider med lunch, framförhållning, horisont och röda dagar. Validering,
avbokningsgräns, bokningsnummer, lösenord och kalenderfil. Hela flödet mot en SQLite-fil, inklusive
tjugo samtidiga bokningar, gallring och att admin inte når en annan verksamhets data. Docker-bygget
kör testerna och stannar om något fallerar.

## Driftsättning

`Dockerfile` och `render.yaml` finns. På Render: New, Blueprint, välj repot.

Gratisnivån har ingen beständig disk, så testmiljön läses in på nytt när tjänsten startar om. För
riktig drift behövs en disk för SQLite-filen eller en databas.

## Nästa steg

- Bekräftelse och påminnelse via e-post och SMS.
- Flera verksamheter per konto och roller för personal.
- Betalning vid bokning för tjänster med förskott.
