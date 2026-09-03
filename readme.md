# HybridDeviceTagger

HybridDeviceTagger ist eine .NET-8-Isolated Azure Function. Sie sucht alle Computer mit `msDS-DeviceID` unterhalb einer konfigurierten Active-Directory-OU, gleicht diese ID mit Microsoft Entra ID ab und setzt bei Hybrid-joined Devices `extensionAttribute1` auf einen definierten Wert.

Vorkonfiguriert sind:

- OU: `OU=Computers,OU=Tier-0,OU=Admin,DC=bloedgelaber,DC=de`
- Attributwert: `Tier 0`
- Zeitplan: alle 15 Minuten

Die Verarbeitung ist idempotent. Ein Graph-PATCH wird nur gesendet, wenn `extensionAttribute1` noch nicht dem Zielwert entspricht. Computer ohne `msDS-DeviceID`, nicht in Entra gefundene Geräte und Geräte ohne `trustType=ServerAd` werden nicht verändert.

## Voraussetzungen

- .NET 8 SDK und Azure Functions Core Tools v4
- Azure Storage oder lokal Azurite für den Timer Trigger
- DNS- und Netzwerkzugriff der Function App auf einen Domain Controller, vorzugsweise LDAPS auf Port 636
- System-assigned oder user-assigned Managed Identity für die Function App
- Microsoft-Graph-Application-Permission `Device.ReadWrite.All` für diese Managed Identity mit Admin Consent
- Ein AD-Servicekonto mit Leserechten auf die konfigurierte OU, sofern die Laufzeit nicht mit integrierten Domänenanmeldedaten arbeitet

Managed Identity authentifiziert den Zugriff auf Microsoft Graph. Sie ersetzt keine LDAP-Anmeldedaten. In Azure sollte `ActiveDirectory__BindPassword` als Key-Vault-Referenz konfiguriert werden.

## Konfiguration

| App Setting | Beschreibung | Vorgabe |
| --- | --- | --- |
| `TaggingSchedule` | NCRONTAB-Ausdruck mit Sekundenfeld | `0 */15 * * * *` |
| `ActiveDirectory__Server` | DNS-Name des Domain Controllers | muss angepasst werden |
| `ActiveDirectory__Port` | LDAP-Port | `636` |
| `ActiveDirectory__UseSsl` | LDAPS aktivieren | `true` |
| `ActiveDirectory__BaseDn` | Suchbasis für Computer | konfigurierte Tier-0-OU |
| `ActiveDirectory__BindUsername` | Servicekonto, idealerweise als UPN | leer für integrierte Anmeldung |
| `ActiveDirectory__BindPassword` | Kennwort des Servicekontos | leer für integrierte Anmeldung |
| `ActiveDirectory__PageSize` | LDAP-Seitengröße | `500` |
| `Tagging__ExtensionAttribute1Value` | Zielwert in Entra ID | `Tier 0` |

Ein explizites Servicekonto verwendet Basic Bind und ist deshalb nur zusammen mit LDAPS zulässig. Die Optionsvalidierung beendet den Start bei unvollständiger oder unsicherer Konfiguration.

## Lokal ausführen

1. `local.settings.example.json` nach `local.settings.json` übertragen und `ActiveDirectory__Server` sowie gegebenenfalls die Bind-Daten anpassen.
2. Azurite starten oder `AzureWebJobsStorage` auf eine Entwicklungs-Storage-Verbindung setzen.
3. Eine lokale Azure-Anmeldung bereitstellen, die über `DefaultAzureCredential` verwendet werden kann.
4. `func start` ausführen.

`local.settings.json` ist von Git ausgeschlossen. Zugangsdaten dürfen nicht in die Beispielkonfiguration oder das Repository eingetragen werden.

## Build und Tests

```powershell
dotnet build HybridDeviceTagger.slnx --configuration Release
dotnet test HybridDeviceTagger.slnx --configuration Release
```

Der Unit-Test prüft in einem Durchlauf die Fälle Update, bereits gesetzt, nicht in Entra gefunden und nicht Hybrid-joined.

## Azure-Betrieb

Die Function App muss aus ihrem virtuellen Netzwerk den Domain Controller per DNS und LDAPS erreichen, beispielsweise über Site-to-Site-VPN oder ExpressRoute. Legen Sie alle Werte aus der Konfiguration als Function-App-Settings an und aktivieren Sie Managed Identity. Weisen Sie deren Service Principal die Graph-App-Rolle `Device.ReadWrite.All` zu und erteilen Sie Admin Consent.

Die Funktion markiert Geräte innerhalb der OU. Sie entfernt das Attribut nicht automatisch, wenn ein Computer später aus der OU verschoben wird.# TimerTrigger - C<span>#</span>

The `TimerTrigger` makes it incredibly easy to have your functions executed on a schedule. This sample demonstrates a simple use case of calling your function every 5 minutes.

## How it works

For a `TimerTrigger` to work, you provide a schedule in the form of a [cron expression](https://en.wikipedia.org/wiki/Cron#CRON_expression)(See the link for full details). A cron expression is a string with 6 separate expressions which represent a given schedule via patterns. The pattern we use to represent every 5 minutes is `0 */5 * * * *`. This, in plain text, means: "When seconds is equal to 0, minutes is divisible by 5, for any hour, day of the month, month, day of the week, or year".

## Learn more

<TODO> Documentation