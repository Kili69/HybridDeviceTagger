# HybridDeviceTagger.Management

`HybridDeviceTagger.Management` ist der PowerShell-Client für die durch Microsoft Entra geschützte
HybridDeviceTagger Management API. Das Modul verwaltet Tagging-Regeln und LDAPS-Vertrauenszertifikate,
liest Betriebszustände und Traces und startet administrative Aktionen.

## Voraussetzungen

- PowerShell 7.2 oder neuer
- Azure CLI (`az`)
- eine aktive Anmeldung über `az login`
- Netzwerkzugriff auf die Function App
- eine passende API-Anwendungsrolle oder eine wirksame Owner-/Contributor-Zuweisung

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
Import-Module ./Deployment/HybridDeviceTagger.Management
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
| `HybridDeviceTagger.Reader` | Regeln, Zertifikatsmetadaten, Status und Trace lesen |
| `HybridDeviceTagger.RuleAdministrator` | Regeln hinzufügen, ersetzen und löschen |
| `HybridDeviceTagger.TaggingOperator` | Geräte-Tagging sofort starten |
| `HybridDeviceTagger.PasswordRotationAdministrator` | Initiales AD-Kennwort setzen und Rotation starten |
| `HybridDeviceTagger.CertificateAdministrator` | LDAPS-Vertrauenszertifikate hinzufügen und löschen |

Wirksame Azure-RBAC-Zuweisungen als **Owner** oder **Contributor** an der Function App gewähren
zusätzlich Regel- und Kennwortverwaltung. Sie gewähren nicht automatisch TaggingOperator,
CertificateAdministrator oder allgemeinen Lesezugriff.

## Sicherheits- und Nebenläufigkeitsmodell

- Zugriffstoken bleiben ausschließlich im Modulzustand des aktuellen Prozesses.
- Kennwörter werden als `SecureString` angenommen und nur unmittelbar für den HTTPS-Request in
  Klartext umgewandelt.
- Zertifikatsbefehle übertragen nur öffentliche Zertifikatsdaten; private Schlüssel werden von der
  API abgelehnt.
- Schreibende Befehle unterstützen `-WhatIf`.
- Lösch- und Kennwortoperationen besitzen `ConfirmImpact = High`.
- Regeln und Zertifikate werden mit `If-Match` und dem zuletzt gelesenen ETag geändert.
- Eine parallele Änderung führt zu einem Konflikt, statt fremde Änderungen zu überschreiben.
- REST-, Authentifizierungs- und Validierungsfehler sind terminierende PowerShell-Fehler.

## Befehlsübersicht

| Befehl | Zweck | Ausgabe |
| --- | --- | --- |
| `Connect-HybridDeviceTagger` | Verbindung und delegiertes Zugriffstoken initialisieren | optional nicht geheime Verbindungsinformationen |
| `Get-HybridDeviceTaggerRule` | alle oder eine benannte Tagging-Regel lesen | Regelobjekte |
| `Add-HybridDeviceTaggerRule` | eine oder mehrere vollständige Regeln hinzufügen oder ersetzen | gespeicherte Konfiguration |
| `Remove-HybridDeviceTaggerRule` | eine Regel löschen | gespeicherte Konfiguration |
| `Get-HybridDeviceTaggerTrustedCertificate` | Metadaten der LDAPS-Vertrauensanker lesen | Zertifikatsmetadaten |
| `Add-HybridDeviceTaggerTrustedCertificate` | öffentliches DER-/PEM-Zertifikat hinzufügen | Zertifikatssammlung |
| `Remove-HybridDeviceTaggerTrustedCertificate` | Zertifikat nach Thumbprint löschen | Zertifikatssammlung |
| `Get-HybridDeviceTaggerLastRunStatus` | letzten Geräte-Tagging-Status lesen | Statusobjekt |
| `Get-HybridDeviceTaggerPasswordRotationStatus` | letzten Kennwortrotationsstatus lesen | Statusobjekt |
| `Get-HybridDeviceTaggerTrace` | letzten JSONL-Trace strukturiert lesen | `HybridDeviceTagger.TraceEvent`-Objekte |
| `Initialize-HybridDeviceTaggerActiveDirectoryCredential` | initiales AD-Kennwort validieren und speichern | Bestätigung |
| `Invoke-HybridDeviceTagging` | Geräte-Tagging sofort ausführen | Invocation-ID und Ergebnis |
| `Invoke-HybridDeviceTaggerPasswordRotation` | Kennwortrotation sofort ausführen | Invocation-ID und Ergebnis |

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

## LDAPS-Vertrauenszertifikate verwalten

```powershell
Get-HybridDeviceTaggerTrustedCertificate

Add-HybridDeviceTaggerTrustedCertificate `
    -Path ./contoso-root-ca.cer `
    -WhatIf

Get-HybridDeviceTaggerTrustedCertificate |
    Where-Object thumbprint -EQ $oldThumbprint |
    Remove-HybridDeviceTaggerTrustedCertificate -Confirm
```

`Add-HybridDeviceTaggerTrustedCertificate` akzeptiert öffentliche DER- oder PEM-Dateien bis 32.768
Bytes. `Remove-HybridDeviceTaggerTrustedCertificate` akzeptiert den Thumbprint direkt oder über die
Pipeline-Eigenschaft `thumbprint`. Leerzeichen und Trennzeichen im SHA-1-Thumbprint sind zulässig.
Die API verhindert das Löschen des letzten Vertrauensankers.

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

## AD-Servicekonto initialisieren

```powershell
$password = Read-Host 'AD-Servicekonto-Kennwort' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential `
    -Password $password `
    -Confirm
```

Die Function prüft das Kennwort durch einen AD-Bind und legt anschließend das Current-Secret über ihre
Managed Identity im Key Vault an. Ein bereits vorhandenes Current-Secret wird nicht überschrieben.

## Administrative Aktionen starten

```powershell
Invoke-HybridDeviceTagging -WhatIf
$taggingResult = Invoke-HybridDeviceTagging

Invoke-HybridDeviceTaggerPasswordRotation -WhatIf
$rotationResult = Invoke-HybridDeviceTaggerPasswordRotation -Confirm
```

Beide Befehle laufen synchron und liefern Invocation-ID sowie Ergebnis. Ein bereits laufender Vorgang
desselben Typs führt zu HTTP `409`. Die erzwungene Kennwortrotation überspringt ausschließlich die
Altersprüfung; aktueller Bind, LDAPS, Staging, Prüfung des neuen Kennworts und Secret-Promotion bleiben
verpflichtend.

## Integrierte Hilfe

Jeder exportierte Befehl enthält Comment-based Help:

```powershell
Get-Command -Module HybridDeviceTagger.Management
Get-Help Connect-HybridDeviceTagger -Full
Get-Help Add-HybridDeviceTaggerRule -Examples
Get-Help Remove-HybridDeviceTaggerTrustedCertificate -Parameter CertificateThumbprint
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
| Zertifikatsdatei zu groß | eine öffentliche DER-/PEM-Datei bis 32.768 Bytes verwenden |

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
| `Get-HybridDeviceTaggerTrustedCertificateSnapshot` | Zertifikatssammlung mit ETag lesen |

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
2. `ModuleVersion` entspricht der Version aus `Directory.Build.props`.
3. Der Release-Tag entspricht exakt `v<ModuleVersion>`.
4. Das Manifest ist gültig und alle deklarierten Funktionen können importiert werden.
5. Dieselbe Modulversion ist noch nicht in der PowerShell Gallery vorhanden.

Der API-Schlüssel wird ausschließlich aus dem GitHub-Repository-Secret
`HYBRIDDEVICETAGGER_PSMODUL` gelesen. Er wird weder in Dateien geschrieben noch als Workflow-Ausgabe
veröffentlicht. Bereits vorhandene Versionen werden bei einer erneuten Workflow-Ausführung
übersprungen.

Das Publishing-Job verwendet die GitHub-Umgebung `psgallery`. Falls diese Umgebung im Repository noch
nicht existiert, wird sie unter **Settings > Environments** angelegt. Dort können optional
Freigaberegeln oder ausschließlich für Releases zugelassene Branches konfiguriert werden.
