# Instagram Public Monitor

Lokaler Prototyp, der den Status von `@thunderceo` ueber den RapidAPI-Endpunkt
`Instagram Looter /profile2` prueft. Ein Ereignis wird nur bei einem echten
Wechsel von `private` zu `public` ausgeloest.

## Desktop-Oberflaeche

`InstagramProfileChecker.exe` startet eine lokale Windows-Oberflaeche ohne
Konsolenfenster. Sie verwaltet mehrere Instagram-Accounts, zeigt je Account
Status, letzten Check und Countdown und scannt aktive Accounts automatisch.
Im Accountdialog reicht `@username`; benoetigte Profil-URLs werden intern erzeugt.
Mehrere Accounts lassen sich per Zeile, Leerzeichen, Komma oder Semikolon sowie
durch den Import einer oder mehrerer `.txt`-Dateien hinzufuegen. Leere Zeilen,
Duplikate und mit `#` beginnende Kommentare werden ignoriert. Der RapidAPI-Key
gilt lokal fuer alle Accounts. Die Oberflaeche passt Karten, Tabellen und Buttons
dynamisch an die Fenstergroesse an; eine Mindestgroesse verhindert abgeschnittene
oder ueberlappende Bedienelemente.

Automatische `private -> public`-Ereignisse besitzen einen konfigurierbaren
Cooldown pro Account. Dadurch erzeugen schnelle Statuswechsel keine doppelten
Events. Manuelle Demo-Ereignisse bleiben jederzeit moeglich. Simulierte Orders
wechseln lokal von `Pending` ueber `In Progress` zu `Completed`; jeder Wechsel
wird nachvollziehbar in `events.jsonl` gespeichert.

Jeder aktive Account verbraucht einen RapidAPI-Request pro Scanintervall. Bei
zehn Accounts und einem 30-Minuten-Intervall entstehen beispielsweise ungefaehr
14.400 Requests pro Monat.

Starten:

```powershell
.\InstagramProfileChecker.exe
```

Nach Aenderungen neu bauen:

```powershell
.\build-exe.ps1
```

## Einrichtung

1. `.env.example` als `.env` kopieren.
2. Den eigenen RapidAPI-Key in `.env` eintragen. Den Key nicht committen oder teilen.
3. Einmal testen:

```powershell
.\start.ps1 -Once
```

4. Dauerhaft starten:

```powershell
.\start.ps1
```

Lokale Panel-Demo ohne RapidAPI-Key oder API-Verbrauch:

```powershell
.\start.ps1 -Demo
```

Standardmaessig wird alle 30 Minuten geprueft. Der Monitor speichert den letzten
Status in `state.json`. Beim Wechsel zu `public` ertönt ein Signal und ein lokales
Dry-Run-Ereignis wird an `events.jsonl` angehaengt. Das Ereignis enthaelt eine
realistisch simulierte JustAnotherPanel-Order mit Demo-ID, Ziel, Menge und Status.

Die simulierte Menge kann in `.env` angepasst werden:

```text
PANEL_SIMULATED_QUANTITY=1000
EVENT_COOLDOWN_MINUTES=1440
```

`mock_panel.py` ist absichtlich ein rein lokaler Prank-Adapter: Er fuehrt keine
HTTP-Anfragen aus, nimmt keinen Panel-Key entgegen und erstellt keine echte Order.

## Tarifhinweis

30-Minuten-Intervalle ergeben rund 1.440 Requests pro Monat. Der kostenlose
Basic-Tarif mit 150 Requests reicht dafuer nicht; fuer Basic sollte das Intervall
mindestens etwa 5 Stunden betragen.

## Tests

```powershell
python -m unittest -v
```

Falls `python` nicht im PATH liegt, kann `start.ps1` automatisch die gebuendelte
Codex-Python-Laufzeit verwenden.
