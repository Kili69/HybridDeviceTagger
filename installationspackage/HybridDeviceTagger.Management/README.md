# HybridDeviceTagger.Management

`HybridDeviceTagger.Management` ist der PowerShell-Client für die durch Microsoft Entra geschützte
HybridDeviceTagger Management API. Das Modul richtet nach der Installation die Active-Directory-Anbindung
ein, verwaltet Tagging-Regeln und LDAP-Vertrauensanker, liest Betriebszustände und Traces und startet
administrative Aktionen.

## Voraussetzungen

- PowerShell 7.2 oder neuer
- Azure CLI (`az`)
- eine aktive Anmeldung über `az login`
- Netzwerkzugriff auf die Function App
- eine passende API-Anwendungsrolle oder eine wirksame Owner-/Contributor-Zuweisung; Forest-Domäne,
  Servicekonto und Vertrauensanker erfordern immer Owner-/Contributor-Rechte an der Function App

Das Modul benötigt keinen direkten Zugriff auf Active Directory, Key Vault, Blob Storage, Microsoft
Graph oder Azure Resource Manager. Die Function App führt diese Operationen mit ihrer Managed Identity
aus.

## Import und Verbindung

Nach der Veröffentlichung kann das Modul direkt aus der PowerShell Gallery installiert werden:

```powershell
Install-PSResource -Name HybridDeviceTagger.Management -Repository PSGallery
Import-Module HybridDeviceTagger.Management
```

Alternativ kann die im Repository enthaltene Version importiert werden:

```powershell
Import-Module ./installationspackage/HybridDeviceTagger.Management
Connect-HybridDeviceTagger
```

Die Standardverbindung verwendet die produktive API-URI und deren nicht geheime Entra-Client-ID. Eine
abweichende Bereitstellung wird explizit angegeben:

```powershell
Connect-HybridDeviceTagger `
    -ApiBaseUri 'https://example.azurewebsites.net/api/management' `
    -ClientId '00000000-0000-0000-0000-000000000000' `
    -PassThru
```

`Connect-HybridDeviceTagger` ruft über Azure CLI ein delegiertes Zugriffstoken für
`api://<client-id>/.default` ab. Ohne `-SkipValidation` liest der Befehl anschließend die
Tagging-Konfiguration. Damit werden Erreichbarkeit, Token-Audience, Easy Auth und Autorisierung sofort
geprüft.

Die Verbindung gilt nur für die aktuelle Modulinstanz im laufenden PowerShell-Prozess. Das Token wird
nicht gespeichert oder ausgegeben und bei weniger als zwei Minuten Restlaufzeit automatisch erneuert.
Zum Trennen der Verbindung wird das Modul entfernt oder die PowerShell-Sitzung beendet:

```powershell
Remove-Module HybridDeviceTagger.Management
```

## Autorisierung

Die Entra-Identität benötigt je nach Operation eine der folgenden Anwendungsrollen:

| Anwendungsrolle | Operationen |
| --- | --- |
| `HybridDeviceTagger.Reader` | Regeln, Status und Trace lesen |
| `HybridDeviceTagger.RuleAdministrator` | Regeln hinzufügen, ersetzen und löschen |
| `HybridDeviceTagger.TaggingOperator` | Geräte-Tagging sofort starten |
| `HybridDeviceTagger.PasswordRotationAdministrator` | Initiales AD-Kennwort setzen und Rotation starten |

Wirksame Azure-RBAC-Zuweisungen als **Owner** oder **Contributor** an der Function App gewähren
zusätzlich Regel- und Kennwortverwaltung sowie das Starten von Tagging-Läufen. Sie gewähren nicht
automatisch allgemeinen Lesezugriff. Die Forest-Domäne können ausschließlich Owner und Contributor ändern, weil
sie bestimmt, welche Server beim Bind das Kennwort des Dienstkontos erhalten.

Die frühere Rolle `HybridDeviceTagger.CertificateAdministrator` wurde entfernt. LDAPS-Vertrauensanker
und LDAP-Servicekonto verwalten ausschließlich Owner und Contributor der Function App, entweder im
Konfigurationsportal unter `https://<function-app>.azurewebsites.net/api/portal` oder mit
`Get-`, `Add-` und `Remove-HybridDeviceTaggerTrustedCertificate` sowie
`Set-HybridDeviceTaggerServiceAccount`. Anwendungsrollen der Management API genügen dafür nicht. Das
Aktivitätsprotokoll steht nur im Konfigurationsportal zur Verfügung.

## Active Directory nach der Installation konfigurieren

Der Installer fragt keine AD-Einstellungen ab. Nach der Installation ist die Function daher
unkonfiguriert: Die Timer für Tagging und Kennwortrotation werden übersprungen, Aktionen und
API-Aufrufe, die Active Directory benötigen, antworten mit HTTP `409`, und
`Get-HybridDeviceTaggerActiveDirectoryConfiguration` liefert die Quelle `notConfigured` mit leerer
Domäne. Die Einrichtung erfolgt in dieser Reihenfolge:

```powershell
Connect-HybridDeviceTagger -ApiBaseUri 'https://<function-app>.azurewebsites.net/api/management' `
    -ClientId '<management-api-client-id>' -SkipValidation

# 1. Root-Zertifikat (Vertrauensanker für LDAPS/StartTLS)
Add-HybridDeviceTaggerTrustedCertificate -Path '.\Contoso-Root-CA.cer'
# 2. Forest-Domäne
Set-HybridDeviceTaggerActiveDirectoryConfiguration -Domain 'contoso.com'
# 3. Servicekonto und Kennwort (wird per LDAP geprüft, daher nach dem Zertifikat)
Set-HybridDeviceTaggerServiceAccount -Credential (Get-Credential 'svc-tagger@contoso.com')
```

Danach (4.) die erste Regel mit `Add-HybridDeviceTaggerRule` anlegen. Dieselben Schritte stehen im
Konfigurationsportal in den Bereichen **Zertifikate**, **LDAP** und **Regeln** zur Verfügung.

## Forest-Domäne festlegen oder korrigieren

Die Forest-Domäne kann auch ohne funktionierende AD-Verbindung und vor der initialen
Kennwortspeicherung festgelegt oder korrigiert werden. Der Client muss nur die Function App erreichen.
`-SkipValidation` überspringt die Verbindungsprüfung, die die Tagging-Regeln liest und dafür
die AD-Verbindung benötigen kann.

```powershell
Connect-HybridDeviceTagger -ApiBaseUri 'https://<function-app>.azurewebsites.net/api/management' `
    -ClientId '<management-api-client-id>' -SkipValidation
Get-HybridDeviceTaggerActiveDirectoryConfiguration
Set-HybridDeviceTaggerActiveDirectoryConfiguration -Domain 'contoso.com' -Confirm
```

Die Änderung benötigt wirksame Owner- oder Contributor-Rechte an der Function App;
Anwendungsrollen der Management API genügen nicht. Alternativ steht sie im Konfigurationsportal zur Verfügung.
Geprüft wird die Syntax des Forest-DNS-Namens, nicht dessen Erreichbarkeit. Die private
Blob-Konfiguration hat Vorrang vor der ursprünglichen App-Einstellung und bleibt bei einer
erneuten Installation erhalten. Neue Forest-Verbindungen lesen sie ohne Function-Neustart;
bereits laufende Vorgänge können noch die alte Domäne verwenden. Regeln, Dienstkonto-UPN,
ein expliziter Dienstkonto-DN und ein manuell konfigurierter Server-Override bleiben unverändert.
Das Kommando verwendet den zuletzt gelesenen ETag für den Schreibzugriff.

## Sicherheits- und Nebenläufigkeitsmodell

- Zugriffstoken bleiben ausschließlich im Modulzustand des aktuellen Prozesses.
- Kennwörter werden als `SecureString` oder `PSCredential` angenommen und nur unmittelbar für den HTTPS-Request in
  Klartext umgewandelt.
- Schreibende Befehle unterstützen `-WhatIf`.
- Lösch- und Kennwortoperationen sowie das Setzen des Servicekontos und das Hinzufügen oder Entfernen
  von Vertrauensankern besitzen `ConfirmImpact = High`.
- Regeln werden mit `If-Match` und dem zuletzt gelesenen ETag geändert.
- Eine parallele Änderung führt zu einem Konflikt, statt fremde Änderungen zu überschreiben.
- REST-, Authentifizierungs- und Validierungsfehler sind terminierende PowerShell-Fehler.

## Befehlsübersicht

| Befehl | Zweck | Ausgabe |
| --- | --- | --- |
| `Connect-HybridDeviceTagger` | Verbindung und delegiertes Zugriffstoken initialisieren | optional nicht geheime Verbindungsinformationen |
| `Get-HybridDeviceTaggerRule` | alle oder eine benannte Tagging-Regel lesen | Regelobjekte |
| `Add-HybridDeviceTaggerRule` | eine oder mehrere vollständige Regeln hinzufügen oder ersetzen | gespeicherte Konfiguration |
| `Remove-HybridDeviceTaggerRule` | eine Regel löschen | gespeicherte Konfiguration |
| `Get-HybridDeviceTaggerLastRunStatus` | letzten Geräte-Tagging-Status lesen | Statusobjekt |
| `Get-HybridDeviceTaggerPasswordRotationStatus` | letzten Kennwortrotationsstatus lesen | Statusobjekt |
| `Get-HybridDeviceTaggerTrace` | letzten JSONL-Trace strukturiert lesen | `HybridDeviceTagger.TraceEvent`-Objekte |
| `Get-HybridDeviceTaggerActiveDirectoryConfiguration` | wirksame Forest-Domäne mit ETag und Quelle lesen | Konfigurationsobjekt |
| `Set-HybridDeviceTaggerActiveDirectoryConfiguration` | Forest-Domäne festlegen oder korrigieren | gespeicherte Konfiguration |
| `Get-HybridDeviceTaggerTrustedCertificate` | LDAP-Vertrauensanker lesen | Objekt mit `certificates`, `eTag` und `source` |
| `Add-HybridDeviceTaggerTrustedCertificate` | öffentliches Zertifikat als LDAP-Vertrauensanker hinzufügen | gespeicherte Vertrauensanker |
| `Remove-HybridDeviceTaggerTrustedCertificate` | Vertrauensanker anhand des SHA-1-Fingerabdrucks entfernen | verbleibende Vertrauensanker |
| `Set-HybridDeviceTaggerServiceAccount` | Servicekonto und Kennwort validieren und speichern oder ersetzen | Objekt mit `username`, `distinguishedName` und `eTag` |
| `Initialize-HybridDeviceTaggerActiveDirectoryCredential` | initiales Kennwort eines bereits konfigurierten Servicekontos validieren und speichern | Bestätigung |
| `Invoke-HybridDeviceTagging` | Geräte-Tagging sofort in die Warteschlange stellen | Invocation-ID, Status `Queued` und Status-URI; mit `-Wait` finaler Status |
| `Invoke-HybridDeviceTaggerPasswordRotation` | Kennwortrotation sofort in die Warteschlange stellen | Invocation-ID, Status `Queued` und Status-URI; mit `-Wait` finaler Status |

## Regeln lesen

```powershell
Get-HybridDeviceTaggerRule
Get-HybridDeviceTaggerRule -Name 'Kiosk devices'
```

Wenn keine Regel mit dem Namen übereinstimmt, erzeugt der Befehl keine Ausgabe. Der Vergleich beachtet
Groß- und Kleinschreibung nicht.

## Regeln hinzufügen oder ersetzen

Eine Regel wird vollständig übergeben. Eine vorhandene Regel mit demselben Namen wird vollständig
ersetzt; alle anderen Regeln bleiben erhalten.

```powershell
$rule = @{
    name = 'Kiosk devices'
    description = 'Tags devices located in the kiosk OU.'
    baseDn = 'OU=Kiosks,OU=Workstations,DC=contoso,DC=com'
    extensionAttributes = @{
        extensionAttribute1 = 'Kiosk'
    }
    entraGroupIds = @()
    administrativeUnitIds = @()
}

Add-HybridDeviceTaggerRule -Rule $rule -WhatIf
Add-HybridDeviceTaggerRule -Rule $rule
```

Mehrere Regeln können über ein Objekt mit einer `Rules`-Eigenschaft verarbeitet werden:

```powershell
$configuration = @{
    Rules = @($kioskRule, $workstationRule)
}

$configuration | Add-HybridDeviceTaggerRule
```

Das Modul akzeptiert pro Aufruf 1 bis 100 Regeln. Vor jeder einzelnen Änderung wird ein neuer Snapshot
gelesen, weil ein erfolgreiches Upsert das ETag der gesamten Sammlung ändert. Die API validiert das
vollständige Regelmodell.

## Regeln löschen

```powershell
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -WhatIf
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -Confirm
```

Die API verhindert, dass durch das Löschen keine gültige Regel mehr verbleibt.

## Betriebsstatus und Trace lesen

```powershell
Get-HybridDeviceTaggerLastRunStatus | Format-List
Get-HybridDeviceTaggerPasswordRotationStatus | Format-List

$events = @(Get-HybridDeviceTaggerTrace)
$events |
    Where-Object level -EQ 'Error' |
    Select-Object timestampUtc, phase, action, details
```

Der Trace wird serverseitig als JSONL gespeichert. Das Modul zerlegt ihn zeilenweise und gibt für jedes
Ereignis ein strukturiertes `HybridDeviceTagger.TraceEvent`-Objekt aus. Die Formatdatei zeigt
standardmäßig:

```text
<Zeit>,<Severity>,<Phase>,<Status>,<Action>,<Details>
```

Die Anzeige verändert das Objekt nicht. Filtern, JSON-Konvertierung und CSV-Export behalten alle
Eigenschaften:

```powershell
Get-HybridDeviceTaggerTrace |
    Select-Object timestampUtc, level, phase, outcome, action, details |
    Export-Csv ./device-tagging-trace.csv -NoTypeInformation
```

## LDAP-Vertrauensanker verwalten

```powershell
(Get-HybridDeviceTaggerTrustedCertificate).certificates |
    Format-Table thumbprint, subject, notAfterUtc
Add-HybridDeviceTaggerTrustedCertificate -Path '.\Contoso-Root-CA.cer' -Confirm
Remove-HybridDeviceTaggerTrustedCertificate -Thumbprint '<SHA-1-Fingerabdruck>' -Confirm
```

`Add-HybridDeviceTaggerTrustedCertificate` lädt eine DER- (`.cer`/`.crt`) oder PEM-Datei lokal,
lehnt Dateien mit privatem Schlüssel und abgelaufene Zertifikate bereits vor dem Request ab und sendet
nur das öffentliche PEM an die Function. Neue LDAP-Verbindungen verwenden die Vertrauensanker sofort.
Der letzte Vertrauensanker kann nicht entfernt werden. Die Befehle verwenden die Portal-Route
`management/portal/certificates` und erfordern wirksame Owner-/Contributor-Rechte an der Function App.

## AD-Servicekonto festlegen

```powershell
Set-HybridDeviceTaggerServiceAccount `
    -Credential (Get-Credential 'svc-tagger@contoso.com') `
    -DistinguishedName 'CN=svc-tagger,OU=Service Accounts,DC=contoso,DC=com' `
    -Confirm
```

Forest-Domäne und Vertrauenszertifikat müssen vorher konfiguriert sein. Die Function prüft den Bind
mit UPN und Kennwort über LDAPS beziehungsweise StartTLS, speichert das Kennwort mit ihrer Managed
Identity im Key Vault und danach das Konto. Ohne `-DistinguishedName` ermittelt sie den DN aus dem
UPN. Ein vorhandenes Konto oder Kennwort wird ersetzt. Der Befehl verwendet die Portal-Route
`management/portal/active-directory/account` und erfordert wirksame Owner-/Contributor-Rechte an der
Function App.

## AD-Servicekonto initialisieren

Ist das Servicekonto bereits konfiguriert, etwa durch Pre-Seeding mit `ADServiceAccount` bei der
Installation, kann alternativ nur das initiale Kennwort gesetzt werden:

```powershell
$password = Read-Host 'AD-Servicekonto-Kennwort' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential `
    -Password $password `
    -Confirm
```

Die Function prüft das Kennwort durch einen AD-Bind und legt anschließend das Current-Secret über ihre
Managed Identity im Key Vault an. Ersetzt werden nur ein fehlendes Secret oder der unbrauchbare Platzhalter,
den die Installation in einem vorhandenen Key Vault anlegt. Ein echtes Kennwort wird nie überschrieben;
die API antwortet dann mit HTTP `409`.

## Administrative Aktionen starten

```powershell
Invoke-HybridDeviceTagging -WhatIf
$accepted = Invoke-HybridDeviceTagging
$taggingStatus = Invoke-HybridDeviceTagging -Wait

Invoke-HybridDeviceTaggerPasswordRotation -WhatIf
$rotationStatus = Invoke-HybridDeviceTaggerPasswordRotation -Wait -TimeoutSeconds 900 -Confirm
```

Beide Befehle laufen asynchron. Die API stellt den Lauf in eine private Storage-Queue und antwortet
sofort mit HTTP `202`, der Invocation-ID, dem Status `Queued` und dem Status-URI. Ohne `-Wait` geben
die Befehle diese Antwort zurück. Mit `-Wait` fragt das Modul alle zehn Sekunden den Statusendpunkt ab,
bis genau dieser Lauf `Succeeded` oder `Failed` erreicht, und gibt den finalen Status zurück.
`-TimeoutSeconds` begrenzt die Wartezeit (30 bis 86.400 Sekunden; Standard 1.800 Sekunden für das
Tagging und 600 Sekunden für die Rotation). Wird die Invocation-ID im Status durch einen neueren Lauf
ersetzt oder die Wartezeit überschritten, endet der Befehl mit einem Fehler. Fehlgeschlagene
angeforderte Läufe werden nicht automatisch wiederholt. Ein bereits laufender Vorgang desselben Typs
führt zu HTTP `409`. Die erzwungene Kennwortrotation überspringt ausschließlich die Altersprüfung;
aktueller Bind, LDAPS, Staging, Prüfung des neuen Kennworts und Secret-Promotion bleiben verpflichtend.

## Integrierte Hilfe

Jeder exportierte Befehl enthält Comment-based Help:

```powershell
Get-Command -Module HybridDeviceTagger.Management
Get-Help Connect-HybridDeviceTagger -Full
Get-Help Add-HybridDeviceTaggerRule -Examples
Get-Help Invoke-HybridDeviceTagging -Parameter Wait
```

## Fehlerbehandlung

| Fehlerbild | Ursache oder Maßnahme |
| --- | --- |
| `Azure CLI is required` | Azure CLI installieren und das Terminal neu öffnen |
| `No HybridDeviceTagger API connection exists` | zuerst `Connect-HybridDeviceTagger` ausführen |
| Token kann nicht abgerufen werden | `az login --scope api://<client-id>/.default` ausführen |
| HTTP `401` | Anmeldung, Token-Audience und Easy-Auth-Konfiguration prüfen |
| HTTP `403` | Anwendungsrolle oder wirksame Owner-/Contributor-Zuweisung prüfen |
| HTTP `409` beim Schreiben | Sammlung wurde parallel geändert; Zustand neu lesen und Änderung wiederholen |
| HTTP `409` bei einer Aktion | ein Vorgang desselben Typs läuft bereits |
| HTTP `409` bei der Kennwortinitialisierung | im Key Vault liegt bereits ein echtes Kennwort |
| HTTP `409` mit `not configured` | Forest-Domäne oder Servicekonto fehlen noch; siehe [Active Directory nach der Installation konfigurieren](#active-directory-nach-der-installation-konfigurieren) |
| `did not finish within ... seconds` | `-TimeoutSeconds` erhöhen oder den Status später erneut abfragen |
| `was superseded by invocation` | ein neuerer Lauf hat den Status überschrieben; Status prüfen und bei Bedarf erneut starten |

Fehler sollten nicht pauschal abgefangen oder als Erfolg behandelt werden. Für Automation empfiehlt
sich ein gezielter `try`/`catch`-Block mit einem von null verschiedenen Prozess-Exitcode:

```powershell
try {
    Connect-HybridDeviceTagger
    Invoke-HybridDeviceTagging
}
catch {
    Write-Error $_
    exit 1
}
```

## Interner Aufbau

Das Modul trennt öffentliche Befehle von fünf privaten Hilfsfunktionen:

| Private Funktion | Verantwortung |
| --- | --- |
| `Initialize-HybridDeviceTaggerToken` | Token über Azure CLI abrufen und Ablaufzeit normalisieren |
| `Assert-HybridDeviceTaggerConnection` | Verbindung prüfen und Token frühzeitig erneuern |
| `Invoke-HybridDeviceTaggerRequest` | URI, Header, JSON, ETag und REST-Fehler zentral behandeln |
| `Get-HybridDeviceTaggerConfigurationSnapshot` | Regelkonfiguration mit ETag lesen |
| `Wait-HybridDeviceTaggerOperation` | Statusendpunkt für `-Wait` abfragen, bis der angeforderte Lauf endet |

`$script:Connection` enthält API-Basis-URI, Client-ID, Zugriffstoken und Ablaufzeit. Der Zustand ist auf
die aktuelle Modulinstanz begrenzt. `Invoke-HybridDeviceTaggerRequest` ist die einzige Stelle, die den
Authorization-Header setzt. Dadurch bleiben Authentifizierung, Serialisierung, Fehlerbehandlung und
optimistische Nebenläufigkeit für alle öffentlichen Befehle konsistent.

## Veröffentlichung auf PowerShell Gallery

Der GitHub-Actions-Workflow
[`publish-powershell-module.yml`](../../.github/workflows/publish-powershell-module.yml) veröffentlicht
das Modul automatisch, sobald ein nicht als Vorabversion markiertes GitHub Release veröffentlicht
wird.

Vor der Veröffentlichung prüft der Workflow:

1. Der Release-Commit ist im Branch `main` enthalten.
2. Der Release-Tag entspricht exakt dem Release-Tag `v<YYYY><MM>.<counter>` des ausgecheckten Commits,
   den `scripts/Get-ProductVersion.ps1` ermittelt.
3. `scripts/Build-InstallationPackage.ps1` baut das Installationspaket und stempelt dabei die
   Produktversion in das Modul-Manifest, das im Quellcode `ModuleVersion = '0.0'` enthält. Die
   gestempelte `ModuleVersion` muss der Produktversion entsprechen.
4. Das Manifest ist gültig und alle deklarierten Funktionen können importiert werden.
5. Dieselbe Modulversion ist noch nicht in der PowerShell Gallery vorhanden.

Veröffentlicht wird das gestempelte Modul aus `artifacts/HybridDeviceTagger-<Version>/HybridDeviceTagger.Management`.

Der API-Schlüssel wird ausschließlich aus dem GitHub-Repository-Secret
`HYBRIDDEVICETAGGER_PSMODUL` gelesen. Er wird weder in Dateien geschrieben noch als Workflow-Ausgabe
veröffentlicht. Bereits vorhandene Versionen werden bei einer erneuten Workflow-Ausführung
übersprungen.

Das Publishing-Job verwendet die GitHub-Umgebung `psgallery`. Falls diese Umgebung im Repository noch
nicht existiert, wird sie unter **Settings > Environments** angelegt. Dort können optional
Freigaberegeln oder ausschließlich für Releases zugelassene Branches konfiguriert werden.
