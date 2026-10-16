# FluxFTP API – enkel protokollspecifikation

Kontrollerad mot källkoden för FluxFTP 2.1.12, 2026-10-16.
Dokumentet beskriver först det API som finns idag. Avsnittet om ett framtida
headless-protokoll är ett förslag och går inte att anropa i nuvarande version.

## Snabbt svar: kan hela stacken köras headless?

**Inte med nuvarande version enbart genom att polla Visionarys HTTP-server.**

- FluxFTP kan styras utan klick via HTTPS när skrivbordsprogrammet är igång,
  exempelvis minimerat till systemfältet. Det är fortfarande en WPF-process i en
  inloggad Windows-session, inte en separat headless-tjänst.
- FluxFTP:s schemaläggare finns separat i Engine, men API-start, jobbskapande,
  sessionpool och status är idag kopplade till `MainWindow` och dess Dispatcher.
- Visionarys `HTTPserver.pm` erbjuder ett CGI/OpenThought-webbgränssnitt.
  Den undersökta filen har ingen REST-kö för jobb, ingen ack/lease och ingen
  ström av chain-händelser som FluxFTP kan konsumera.
- FluxFTP har idag ingen inbyggd poller för Visionarys HTTP-server.

En HTTP-klient skulle kunna anropa dess befintliga webbrutter, men det skapar
inte automatiskt en tillförlitlig jobbintegration eller ersätter mIRC:s inflöde.

## 1. Anslutning och autentisering

Aktivera HTTPS/JSON API i Global Settings. Standardporten är **55477**,
normalt endast på IPv4-loopback:

```text
https://127.0.0.1:55477
```

Alla anrop, även `/info`, kräver HTTP Basic authentication:

```text
Authorization: Basic base64(api:DITT_API_LÖSENORD)
Content-Type: application/json
```

Användarnamnet är valfritt; servern kontrollerar lösenordet efter första kolon.
API-lösenordet är separat från FTP-inloggningarna. Fel autentisering ger
HTTP 401 och `{"error":"Unauthorized"}`. Servern använder ett lokalt
självsignerat certifikat. Exemplens `-k` accepterar detta; för en riktig
integration bör klienten lita på rätt certifikat.

```powershell
curl.exe -k --user "api:DITT_API_LÖSENORD" https://127.0.0.1:55477/info
```

Ingen `/api/v1`-prefix finns idag. JSON-svar använder normalt camelCase;
fält med uttrycklig mappning, till exempel `job_ids`, använder snake_case.

## 2. Livscykel: request → ack → progress → abort/done

| Steg | Nuvarande anrop | Vad svaret betyder |
|---|---|---|
| Job request | `POST /transferjobs` | Inventerar källan och köar filjobb |
| Job ack | Svaret från samma POST | Returnerar filjobbens UUID:er; inte att överföringen är klar |
| Progress | `GET /transferjobs` | Klienten pollar och matchar UUID |
| Abort | `POST /transferjobs/{id}/abort` | Begär borttagning/avbrott av ett filjobb |
| Done | `state == "Completed"` i jobblistan | Filjobbet är klart; ingen separat done-push finns |

En begäran om en katalog kan skapa många filjobb. Det finns inget gemensamt
beständigt request-ID för dessa i dagens vanliga transfer-API.

### Job request

```http
POST /transferjobs
```

```json
{
  "src_site": "SOURCE",
  "src_path": "/incoming",
  "dst_site": "TARGET",
  "dst_path": "/archive",
  "name": "Example.Release-GROUP"
}
```

Detta kopierar `/incoming/Example.Release-GROUP` till
`/archive/Example.Release-GROUP`. Namnet kan vara en fil eller katalog.
Sajterna måste redan finnas i FluxFTP.

| Fält | Betydelse |
|---|---|
| `src_site`, `dst_site` | Obligatoriska sparade sajtnamn |
| `name` | Obligatoriskt fil-/katalognamn som läggs till båda basvägarna |
| `src_path`, `dst_path` | Basvägar; `/` om inget annat anges |
| `src_section`, `dst_section` | Alternativ till basväg: använd sparad sektionsmappning |
| `sourceDownloadSlots`, `destinationUploadSlots` | Finns i requestmodellen för bryggans live-slots; vanliga REST-jobb tillämpar dem inte via Visionarys live-slots-callback |

Om både section och path anges har section företräde på respektive sida.
Vanliga REST-jobb är en inventering/köning, inte en Visionary-watch som väntar
på kommande filer. Skiplist, sajträttigheter, TLS och målfilspolicy gäller.

Spara JSON i `job.json` och kör:

```powershell
curl.exe -k --user "api:DITT_API_LÖSENORD" -H "Content-Type: application/json" --data-binary "@job.json" https://127.0.0.1:55477/transferjobs
```

### Job ack

Exempel på HTTP 200:

```json
{
  "name": "Example.Release-GROUP",
  "status": "QUEUED",
  "files": 1,
  "source": "SOURCE",
  "target": "TARGET",
  "job_ids": ["11111111-2222-4333-8444-555555555555"]
}
```

Spara `job_ids`. Svaret kommer efter källans inventering, inte omedelbart vid
mottagandet. Samma requestmodell kan internt ge `WAITING` eller `SKIPPED` för
race-flöden; räkna inte med sådana watch-svar från vanlig REST-transfer.

**Ingen idempotensnyckel finns.** Om klientens POST timeoutar kan jobbet ändå
ha köats. Skicka därför inte automatiskt samma POST igen utan avstämning.
Fel från jobbskapandet har idag inte en enhetlig JSON-felmodell; en del
validerings-/FTP-fel blir HTTP 500. Tolka inte alla 500 som säkert oköade jobb.

### Progress

```powershell
curl.exe -k --user "api:DITT_API_LÖSENORD" https://127.0.0.1:55477/transferjobs
```

Exempel på ett element i svarsarrayen:

```json
{
  "id": "11111111-2222-4333-8444-555555555555",
  "queued": "2026-10-16 12:00:00",
  "started": "2026-10-16 12:00:01",
  "use": "API FXP",
  "type": "FXP",
  "name": "example.rar",
  "route": "/incoming/Example.Release-GROUP/example.rar → /archive/Example.Release-GROUP/example.rar",
  "size": "50 MB",
  "files": "1",
  "left": "25 MB",
  "speed": "≈10,00 MB/s",
  "done": "≈50%",
  "state": "Transferring",
  "progressPercent": 50
}
```

Exemplet är illustrativt. Storlekar, tider och hastigheter är visningstext och
kan variera med språkinställning. `progressPercent` är ett numeriskt värde.
Det finns idag inga separata numeriska bytes-/hastighetsfält i detta svar.
Tiderna är lokal tid utan tidszon; `—` betyder att värde saknas.

Rekommenderat klientintervall: cirka **1 sekund**. Detta är en rekommendation,
inte ett serverkrav. Det finns ingen SSE, WebSocket eller webhook för progress.

| `state` | Tolkning |
|---|---|
| `Queued` | Köat |
| `Transferring` | Kör |
| `Paused` | Pausat |
| `Completed` | Klart |
| `Skipped` | Överhoppat enligt policy; inte ett bevis på lyckad uppladdning |
| `Failed` | Misslyckat; kan saknas efter automatisk borttagning |

FXP-procent och hastighet kan vara uppskattningar, markerade `≈` i textfälten.
Uppskattningar hålls under 100 procent tills jobbet är klart. Ett avslutat
filjobb och en färdig, verifierad hel release är olika nivåer av completion.

**Begränsningar:** aktiva Visionary-filjobb filtreras bort från denna lista.
Vissa feljobb sparas tillfälligt för hard reset, andra tas bort. Om ett UUID
försvinner betyder det därför **okänd status**, inte automatiskt success eller
abort. Klienten behöver hålla egen historik; API:t har inget beständigt
resultatarkiv eller `GET /transferjobs/{id}`.

### Abort

```http
POST /transferjobs/11111111-2222-4333-8444-555555555555/abort
```

```json
{"aborted":"11111111-2222-4333-8444-555555555555"}
```

HTTP 200 betyder att borttagningsanropet behandlats. Samma svar ges även för
ett okänt UUID. Svaret är ingen separat bekräftelse på att alla FTP-operationer
redan hunnit avslutas. Jobbet tas bort ur kön. För en vanlig katalogrequest
måste klienten anropa abort för varje UUID i `job_ids`.

Detta är ett filjobbavbrott. Det stoppar inte en hel Visionary-watch, som kan
köa nya jobb vid nästa uppdatering. Något REST-anrop för watch-/spread-abort
finns inte idag.

### Reset och done

`POST /transferjobs/{id}/reset` svarar `{"reset":"UUID"}` och återupptar/
schemalägger en befintlig köpost. Okänt UUID ger också HTTP 200.
Detta är **inte** de nya hard reset-kontrollerna med cooldown/lookback.
De har idag inget eget REST-anrop.

Done avläses genom `state == "Completed"`. Spara resultatet i klienten när det
observeras. Det finns ingen `POST /done` eller separat terminalkvittens.

## 3. Spread-jobb för flera sajter

```http
POST /spreadjobs
```

```json
{
  "section": "GAMES",
  "name": "Example.Release-GROUP",
  "sites": ["SOURCE", "TARGET1", "TARGET2"],
  "sites_dlonly": ["SOURCE"]
}
```

Minst två sajtnamn krävs. Sektionen behöver rätt mappning på sajterna.
`sites_dlonly` anger sajter som endast får fungera som källa.
`profile` finns som indatafält men används inte av nuvarande handler.

Ack, HTTP 200:

```json
{"id":1,"state":"STARTED","name":"Example.Release-GROUP","sites":["SOURCE","TARGET1","TARGET2"]}
```

Polla `GET /spreadjobs` eller `GET /spreadjobs/{URL-kodat releasenamn}`.
Valfritt filter: `/spreadjobs?status=RUNNING`.

```json
{
  "id": 1,
  "name": "Example.Release-GROUP",
  "section": "GAMES",
  "status": "RUNNING",
  "sites": ["SOURCE", "TARGET1", "TARGET2"],
  "sites_incomplete": ["TARGET2"],
  "size_estimated_bytes": 0,
  "time_spent_seconds": 12,
  "error": ""
}
```

Status är `RUNNING`, `DONE`, `FAILED` eller `ABORTED`. `DONE` kommer från
koordinatorns completionkontroll. `size_estimated_bytes` är idag alltid 0;
det är inget användbart progressmått. Tid är tid sedan skapande och fortsätter
beräknas även efter avslut. Historiken är i minnet och försvinner vid omstart.

HTTP 400: ogiltig request; 409: samma release kör redan; 429: gränsen för aktiva
spread-jobb nådd; 503: race-callback saknas. Det finns inget spread-abort-anrop.

Från 2.1.11 kan gränsen ställas under **Jobs & history → Maximum active
spread jobs**, 1–2048, standard 32. HTTP 429 innehåller då `error` och `limit`.
Detta gäller HTTPS `/spreadjobs`; Visionary-chains har en separat watch-kö.
Sajternas upload-/download-slots gäller fortfarande. Spara mellan race eftersom
inställningsändringar startar om API:t och avbryter dess aktiva race.

## 4. Övriga HTTPS-anrop

| Anrop | Användning |
|---|---|
| `GET /info` | Namn, version och API-information |
| `GET /sites`, `GET /sites/{name}` | Sajtnamn respektive sajtdetaljer |
| `POST /sites`, `PATCH /sites/{name}`, `DELETE /sites/{name}` | Ändra sparade sajter |
| `GET /sections`, `GET /sections/{name}` | Sektioner |
| `POST /sections`, `PATCH /sections/{name}`, `DELETE /sections/{name}` | Ändra sektioner |
| `GET /sites/{site}/sections[/{name}]` | Sajtens sektionsvägar |
| `POST /sites/{site}/sections` | Lägg till en sektionsväg |
| `PATCH /sites/{site}/sections/{name}`, `DELETE /sites/{site}/sections/{name}` | Ändra/ta bort vägen |
| `GET /path?site=SOURCE&path=%2Fincoming` | FTP-listning, inklusive size/nuke-information |
| `GET /file?site=SOURCE&path=%2Fincoming%2Fexample.nfo` | Hämta liten fil, svar som binärdata |
| `POST /raw` | Kör FTP-kommando på angivna sajter |
| `POST /downloads` | Köa lokal nedladdning |
| `POST /irc/connect` | Spara/starta IRC-anslutning; se [IRC-exempel](irc-api.md) |

`/path` och `/file` öppnar egna FTP-sessioner; använd inte dessa som tät
progresspollning. Valfri `timeout` är 1–300 sekunder, standard 60.
`/file` avvisar över 500 KiB efter hämtning till minne.

Raw-exempel:

```json
{"command":"SITE WKUP exampleuser","sites":["TARGET"],"timeout":30}
```

Svaret från `/raw` har arrayerna `failures`, `successes` och `dupes`.
Läs utfallet per sajt även om HTTP-anropet lyckades.

Download-exempel:

```json
{"site":"SOURCE","remote_path":"/incoming/example.nfo","local_path":"C:/Downloads","recursive":true}
```

Alternativet `remote_section` använder sektionsmappningen. Ack innehåller
`site`, `description`, `remote_path`, `local_path`, `queued`, `status`, men
inga `job_ids`; avstämningen är därför svagare än för FXP-requesten.

## 5. Visionary-bryggans lokala protokoll

Windows named pipe: `\\.\pipe\FluxFTP.Visionary.v1`, endast aktuell Windows-
användare. En UTF-8-rad per request, med TAB mellan fälten och radslut efteråt.
`<TAB>` nedan betyder ett faktiskt tabulatortecken.

```text
TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target
TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target<TAB>chainTime<TAB>dllTime
TRANSFER<TAB>section<TAB>release<TAB>source<TAB>target<TAB>chainTime<TAB>dllTime<TAB>sourceDlSlots<TAB>targetUlSlots
CHAINS<TAB>section<TAB>release<TAB>pairs<TAB>chainTime<TAB>dllTime
```

`pairs` är kommaseparerade `SOURCE;TARGET` eller
`SOURCE;TARGET;sourceDlSlots;targetUlSlots`. `-` betyder okänt slotvärde.
Tidsfälten är bryggans diagnostiska tidsvärden, inte jobb-ID:n. Använd den
medföljande bryggan för dessa värden. Högst 256 par tillåts i en batch.

| Svar | Betydelse |
|---|---|
| `OK RECEIVED N chain pairs; ...` | Batchformatet godtaget; respektive par kan fortfarande misslyckas senare |
| `OK ACCEPTED ...` | Race accepterat för kö/watch |
| `OK WATCH UPDATED ...` | Befintlig watch väckt/uppdaterad |
| `ERROR ...` | Begäran avvisad |

`TRANSFER` med target `@UPDATE` väcker relaterade watches.
Target `@DONE` registrerar en extern complete-signal. Svaret är
`OK DONE RECORDED ...`; **målfilernas verifiering krävs fortfarande**.
Detta är en inkommande signal till Flux, inte ett utgående done-event.
Pipeprotokollet saknar progressström, externt watch-ID och abortkommando.

Från 2.1.12 finns **Jobs & history → Visionary concurrent scheduling**
(1–2048, standard 32). Gränsen styr samtidiga inventerings-/schemaläggningssteg,
inte totalt antal watches eller FTP-anslutningar. Väntande watches släpper sin
plats. Sajternas slots och den separata backloggränsen gäller fortfarande.
Spara mellan race: ändringen startar om bryggan och stoppar aktiva watches.

Det finns också UDP på API-porten när HTTPS-API:t startas:

```text
LÖSENORD fxp SOURCE /source TARGET /target
LÖSENORD download SOURCE /remote/file
LÖSENORD raw TARGET SITE WKUP exampleuser
LÖSENORD race SECTION Release-GROUP SOURCE;TARGET
```

UDP skickar lösenord/kommandon i klartext, saknar leveransgaranti och passar
sämre än HTTPS för en ny integration. Det exponerar ingen progress-/done-ström.

## 6. Visionarys HTTPserver.pm: vad kan pollas idag?

Granskad lokal fil: `C:/FluxFTP/visionary/modules/HTTPserver.pm`.
Den autentiserar med `User_Files/passwd` och routar CGI-anrop till funktioner
som genererar HTML/OpenThought-svar, exempelvis:

| Rutt | Funktion |
|---|---|
| `/main_page` | Webbvy |
| `/fill_sitelist`, `/change_site`, `/change_sect` | Sajter/sektioner i webbeditorn |
| `/do_main_check` | Kör regelkontroll för `section` och `rlsname`, renderar resultat |
| `/fill_chain_sections`, `/change_chain_section` | Chain-konfiguration i webbgränssnittet |
| `/save_chain`, `/save_chain_file` | Sparar chain-konfiguration |

`/do_main_check` anropar `Check_Sites->return_sites(..., "CHECK")` och visar
bland annat CHAINS i ett renderat svar. Det är **inte** en kö över nya racejobb.
Att skrapa den sidan riskerar att upprepa kontroller och saknar cursor,
unika event-ID:n och konsumtionskvittens. Inga `/jobs`, `/events`, `/ack`,
`/progress`, `/abort` eller `/done` finns i denna moduls dispatch-tabell.
Anpassade `Cust_html/*.pl` kan finnas separat; de är inte därmed ett etablerat
jobb-REST-API. Ingen ändring av Visionary har gjorts för dokumentationen.

## 7. Förslag: minsta tillägg för en tillförlitlig headless-stack

**Följande är ett förslag, inte implementerade anrop.**

```text
IRC/annonskälla → Visionary-regler → beständig jobbkorg → Flux-worker → FTP/FTPS
                                              ↑             |
                                              └─ ack/status ┘
```

1. Lägg en beständig jobbkorg efter Visionarys regel-/chainbeslut.
   Varje jobb får UUID, eventsekvens och en idempotensnyckel. Återanvänd live-
   slots och regler som bryggan använder idag; skrapa inte webbgränssnittet.
2. Lägg till en separat Flux-host utan WPF. Flytta jobborkestrering, sessionpool,
   status och API-livscykel från `MainWindow` till gemensamma tjänster.
3. Exponera alla race-/filjobb med numeriska värden, terminalhistorik och abort
   av hela race. Håll externa jobb-ID:n stabila över omstart och retries.
4. Ersätt mIRC som nödvändig annons-/bridgeprocess med ett testat IRC-/ingestflöde.
   En GUI-fri Flux-host ensam gör inte hela Visionary-stacken headless.

Föreslaget transportkontrakt:

| Meddelande | Föreslaget anrop | Obligatorisk betydelse |
|---|---|---|
| Job request | `GET /api/v1/jobs?after=cursor` på producenten | Beständiga, sekvensnumrerade inkommande jobb |
| Job ack | `POST /api/v1/jobs/{id}/ack` | Konsument/lease registrerad efter att Flux sparat jobbet |
| Progress | `POST /api/v1/jobs/{id}/progress` | Monotont eventnummer, fas, bytes, total, B/s och estimated-flagga |
| Abort request | `POST /api/v1/jobs/{id}/abort` | Sparad avbrottsbegäran; worker läser även medan jobb kör |
| Done | `POST /api/v1/jobs/{id}/done` | Terminalt resultat: completed/failed/aborted/skipped, verifiering och felorsak |

Exempel på föreslagen job request:

```json
{
  "job_id":"11111111-2222-4333-8444-555555555555",
  "sequence":42,
  "idempotency_key":"race-instance-42:SOURCE:TARGET",
  "kind":"race",
  "section":"GAMES",
  "release":"Example.Release-GROUP",
  "source":"SOURCE",
  "target":"TARGET",
  "limits":{"source_download":3,"target_upload":2}
}
```

Polling ska vara minst-en-gång med idempotent konsumtion, inte ett löfte om
exakt-en-gång-leverans. Lease med timeout låter jobb återhämtas om worker dör.
Progress ska skilja uppskattade bytes från verifierade bytes. Done måste
sparas innan det kvitteras; dubletter av samma event får samma resultat.
Abort behöver kvittens och därefter terminal `aborted`, inte bara HTTP 200.

Praktisk ordning: bygg först det gemensamma jobb-/statuslagret och en Flux-
headless-host, därefter Visionarys jobbkorg och poller. Det ger en tydlig
gräns även när Visionary senare ersätts av en modern version.

## Källor i projektet

- `src/IoFtp.Desktop/Services/ApiServer.cs`: HTTPS-rutter, autentisering, modeller.
- `src/IoFtp.Desktop/MainWindow.xaml.cs`: API-jobbskapande, status, reset/abort.
- `src/IoFtp.Desktop/Models/TransferJobInfo.cs`: progressmodell.
- `src/IoFtp.Desktop/Services/VisionaryPipeServer.cs`: pipe/batch/complete-signaler.
- `src/IoFtp.Desktop/Services/UdpControlServer.cs`: UDP-inramning.
- `src/IoFtp.Desktop/Services/SpreadRaceCoordinator.cs`: spread-status.
- `src/IoFtp.Desktop/App.xaml` och `App.xaml.cs`: WPF-start, ingen headless-host.

Dokumentationen är verifierad genom kodgranskning. Exemplen har inte skickats
mot skarpa FTP-sajter och inga nya API-anrop har implementerats.
