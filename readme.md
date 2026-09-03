# HybridDeviceTagger

HybridDeviceTagger ist eine .NET-8-Isolated Azure Function. Sie sucht alle Computer mit `msDS-DeviceID` unterhalb einer konfigurierten Active-Directory-OU, gleicht diese ID mit Microsoft Entra ID ab und setzt bei Hybrid-joined Devices `extensionAttribute1` auf einen definierten Wert.

Vorkonfiguriert sind:

- OU: `OU=Computers,OU=Tier-0,OU=Admin,DC=bloedgelaber,DC=de`
- Attributwert: `Tier 0`
- Geräteabgleich: alle 15 Minuten
- Kennwortprüfung: täglich; Rotation nach 25 Tagen

Die Verarbeitung ist idempotent. Ein Graph-PATCH wird nur gesendet, wenn `extensionAttribute1` noch nicht dem Zielwert entspricht. Computer ohne `msDS-DeviceID`, nicht in Entra gefundene Geräte und Geräte ohne `trustType=ServerAd` werden nicht verändert.

## Voraussetzungen

- .NET 8 SDK und Azure Functions Core Tools v4
- Azure Storage oder lokal Azurite für den Timer Trigger
- DNS- und Netzwerkzugriff der Function App auf einen Domain Controller, vorzugsweise LDAPS auf Port 636
- System-assigned oder user-assigned Managed Identity für die Function App
- Microsoft-Graph-Application-Permission `Device.ReadWrite.All` für diese Managed Identity mit Admin Consent
- Ein AD-Servicekonto mit Leserechten auf die konfigurierte OU und dem Recht, das eigene Kennwort zu ändern
- Ein Azure Key Vault für die beiden Kennwort-Secrets

Managed Identity authentifiziert den Zugriff auf Microsoft Graph und Azure Key Vault. Sie ersetzt keine LDAP-Anmeldedaten. Die Function liest das AD-Kennwort bei jedem Lauf direkt aus Key Vault, damit ein Versionswechsel sofort wirksam wird.

## Konfiguration

| App Setting | Beschreibung | Vorgabe |
| --- | --- | --- |
| `TaggingSchedule` | NCRONTAB-Ausdruck mit Sekundenfeld | `0 */15 * * * *` |
| `PasswordRotationSchedule` | Tägliche Rotationsprüfung, standardmäßig 02:30 UTC | `0 30 2 * * *` |
| `ActiveDirectory__Server` | DNS-Name des Domain Controllers | muss angepasst werden |
| `ActiveDirectory__Port` | LDAP-Port | `636` |
| `ActiveDirectory__UseSsl` | LDAPS aktivieren | `true` |
| `ActiveDirectory__BaseDn` | Suchbasis für Computer | konfigurierte Tier-0-OU |
| `ActiveDirectory__BindUsername` | UPN des Servicekontos | `svc-HybridDeviceTagger@bloedgelaber.de` |
| `ActiveDirectory__BindUserDistinguishedName` | DN des Servicekontos für `unicodePwd` | muss angepasst werden |
| `ActiveDirectory__PageSize` | LDAP-Seitengröße | `500` |
| `KeyVault__VaultUri` | URI des Key Vaults | muss angepasst werden |
| `KeyVault__CurrentPasswordSecretName` | Aktuell verwendetes Kennwort | `hybrid-device-tagger-ad-password-current` |
| `KeyVault__NextPasswordSecretName` | Staging- und Recovery-Kennwort | `hybrid-device-tagger-ad-password-next` |
| `PasswordRotation__RotateAfterDays` | Alter bei regulärer Rotation | `25` |
| `PasswordRotation__PasswordLength` | Länge des generierten Kennworts | `40` |
| `PasswordRotation__SecretExpiryDays` | Ablauf einer Secret-Version | `35` |
| `Tagging__ExtensionAttribute1Value` | Zielwert in Entra ID | `Tier 0` |

Das Servicekonto verwendet Basic Bind und ist deshalb nur zusammen mit LDAPS zulässig. Die Optionsvalidierung beendet den Start bei unvollständiger oder unsicherer Konfiguration.

## Erstmalige Einrichtung des AD-Kennworts

Vor dem ersten Start muss das bestehende Kennwort des Servicekontos einmalig als Secret `hybrid-device-tagger-ad-password-current` im Key Vault angelegt werden. `next` wird von der Rotation automatisch erstellt und darf nicht manuell als primäres Kennwort verwendet werden.

Die Managed Identity benötigt auf Secret-Ebene mindestens die Data Actions zum Lesen und Setzen von Secrets. `Key Vault Secrets User` reicht nicht aus, weil diese Rolle kein neues Secret setzen kann. Verwenden Sie eine eng begrenzte benutzerdefinierte Rolle oder, falls das organisatorisch nicht möglich ist, `Key Vault Secrets Officer`.

Das AD-Konto bleibt reguläres Mitglied von `Domain Users`. Zusätzlich müssen auf dem Konto `SELF / Change Password` erlaubt und `User cannot change password` deaktiviert sein. Administrative AD-Gruppen und Reset-Password-Rechte werden nicht benötigt.

## Rotationsablauf

Die Rotation prüft das aktuelle Secret täglich und rotiert regulär ab einem Alter von 25 Tagen:

1. Neues zufälliges Kennwort als `next` in Key Vault speichern.
2. Eigenes AD-Kennwort über LDAPS mit einer kombinierten `unicodePwd`-Delete/Add-Operation ändern.
3. LDAP-Bind mit `next` validieren.
4. Das validierte Kennwort als neue Version von `current` speichern.

Wenn `current` nach einem unterbrochenen Lauf nicht mehr funktioniert, testet die Function zuerst `next` und übernimmt es bei Erfolg als `current`. Auch die Geräteabfrage verwendet diesen Fallback.

## Lokal ausführen

1. `local.settings.example.json` nach `local.settings.json` übertragen und Domain Controller, Benutzer-DN sowie Vault-URI anpassen.
2. Azurite starten oder `AzureWebJobsStorage` auf eine Entwicklungs-Storage-Verbindung setzen.
3. Das bestehende AD-Kennwort als `current`-Secret im Key Vault anlegen.
4. Eine lokale Azure-Anmeldung mit Secret-Lese- und Schreibrechten bereitstellen, die über `DefaultAzureCredential` verwendet werden kann.
5. `func start` ausführen.

`local.settings.json` ist von Git ausgeschlossen. Zugangsdaten dürfen nicht in die Beispielkonfiguration oder das Repository eingetragen werden.

## Build und Tests

```powershell
dotnet build HybridDeviceTagger.sln --configuration Release
dotnet test HybridDeviceTagger.sln --configuration Release
```

Die Unit-Tests prüfen Geräteabgleich, Fälligkeit, reguläre Rotation, Recovery und Kennwortzusammensetzung.

## Azure-Betrieb

Die Function App muss aus ihrem virtuellen Netzwerk den Domain Controller per DNS und LDAPS erreichen, beispielsweise über Site-to-Site-VPN oder ExpressRoute. Legen Sie alle Werte aus der Konfiguration als Function-App-Settings an und aktivieren Sie Managed Identity. Weisen Sie deren Service Principal die Graph-App-Rolle `Device.ReadWrite.All` sowie die beschriebenen Key-Vault-Rechte zu.

Die Funktion markiert Geräte innerhalb der OU. Sie entfernt das Attribut nicht automatisch, wenn ein Computer später aus der OU verschoben wird.