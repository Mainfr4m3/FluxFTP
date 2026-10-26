# Automatisk sajtövervakning

Aktivera **Global Settings > Site monitor > Automatically pause and resume trade**. Funktionen övervakar FTP och FTPS i Windows-klienten och är avstängd som standard.

Standardvärden:

- Kontroll av fungerande sajt: var 300:e sekund.
- Ny kontroll av en sajt som inte svarar: var 60:e sekund.
- Trade pausas efter två anslutningsfel. Flera fel från samma samtidiga anslutningsförsök räknas som ett fel.

Kontrollen använder `NOOP` på en ledig poolad eller delad listningsanslutning. En tillfällig inloggning används när det behövs och det finns ledigt utrymme enligt FluxFTP:s sajtgränser. Den stängs efter kontrollen. Högst två sajter kontrolleras samtidigt. Upptagna anslutningar får arbeta klart; slots som används av andra FTP-klienter kan FluxFTP inte räkna lokalt.

Vid upprepade anslutningsfel visas **SITE OFFLINE [sajt] — trade paused**. Nya överföringar till och från sajten väntar i kön. Pågående överföringar avbryts inte av övervakningen. Övriga sajter kan fortsätta.

När en kontroll eller en ny lyckad inloggning bekräftar kontakt visas **SITE ONLINE [sajt] — trade resumed**. Väntande jobb får schemaläggas igen. Manuellt pausade jobb, manuellt frånkopplade sajter, riktningstillstånd och slotgränser behålls. Racebevakningarnas ordinarie maximala livstid gäller fortfarande; en avslutad bevakning startas inte om automatiskt.

Fulla login-slots innebär inte att servern är nere. Inte heller filfel som 550/553 eller en användaravbruten överföring sätter sajten offline. Inloggnings- och TLS-fel visas separat som **LOGIN FAILED**, pausar trade och väntar minst tio minuter mellan automatiska försök. Kontrollerna skickar inte `SITE USER`.

Öppna **Tools > SITE STATUS**, eller **Metrics > Show Status**, för aktuell hälsostatus, senaste/nästa kontroll och felräknare. **Check all sites now** begär en omedelbar kontroll när övervakningen är aktiverad. Status och automatiska pauser gäller den aktuella FluxFTP-sessionen; inga permanenta sajtinställningar skrivs om när en sajt går ner eller kommer tillbaka.
