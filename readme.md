# HybridDeviceTagger

HybridDeviceTagger ist eine .NET-8-Isolated Azure Function. Sie durchsucht eine oder mehrere konfigurierte Active-Directory-OUs einschließlich aller untergeordneten OUs nach synchronisierten Computerobjekten mit `userCertificate`. Die AD-`objectGUID` wird mit der Entra-`deviceId` abgeglichen; passende Geräte erhalten regelabhängig Werte für `extensionAttribute1` bis `extensionAttribute15` und werden optional Entra-Sicherheitsgruppen sowie normalen oder eingeschränkten administrativen Einheiten hinzugefügt.

Vorkonfiguriert sind:

- Regeln: mehrere OU-Teilbäume mit jeweils einem oder mehreren Extension-Attributen
- Geräteabgleich: alle 15 Minuten
- Kennwortprüfung: täglich; Rotation nach 25 Tagen

Die Verarbeitung ist idempotent. Ein Graph-PATCH wird nur für Extension-Attribute gesendet, deren bestehender Wert vom Zielwert abweicht. Computer ohne `userCertificate`, nicht in Entra gefundene Geräte und Geräte ohne `trustType=ServerAd` werden nicht verändert.

## Voraussetzungen

- .NET 8 SDK und Azure Functions Core Tools v4
- Azure Storage oder lokal Azurite für den Timer Trigger
- DNS-Auflösung und Netzwerkzugriff der Function App auf die DNS-Namen aller betroffenen Forest-Domains über LDAPS auf Port 636 oder ersatzweise LDAP auf Port 389
- System-assigned oder user-assigned Managed Identity für die Function App
- Microsoft-Graph-Application-Permissions `Device.ReadWrite.All`, `GroupMember.ReadWrite.All` und `AdministrativeUnit.ReadWrite.All` für diese Managed Identity mit Admin Consent
- Ein forestweit verwendbares AD-Servicekonto mit Leserechten auf die konfigurierten OUs in allen Domains und dem Recht, das eigene Kennwort zu ändern
- Ein Azure Key Vault für die beiden Kennwort-Secrets

Managed Identity authentifiziert den Zugriff auf Microsoft Graph und Azure Key Vault. Sie ersetzt keine LDAP-Anmeldedaten. Die Function liest das AD-Kennwort bei jedem Lauf direkt aus Key Vault, damit ein Versionswechsel sofort wirksam wird.

## Konfiguration

| App Setting | Beschreibung | Vorgabe |
| --- | --- | --- |
| `TaggingSchedule` | NCRONTAB-Ausdruck mit Sekundenfeld | `0 */15 * * * *` |
| `PasswordRotationSchedule` | Tägliche Rotationsprüfung, standardmäßig 02:30 UTC | `0 30 2 * * *` |
| `ActiveDirectory__Server` | DNS-Name eines Domain Controllers im Forest | muss angepasst werden |
| `ActiveDirectory__Port` | LDAP-Port | `636` |
| `ActiveDirectory__UseSsl` | LDAPS aktivieren | `true` |
| `ActiveDirectory__AllowLdapFallback` | Bei nicht verfügbarem LDAPS Lese- und Bindzugriffe über LDAP erlauben | `false`; der Installer ermittelt den Wert |
| `ActiveDirectory__LdapFallbackPort` | Port für den signierten und versiegelten LDAP-Negotiate-Fallback | `389` |
| `ActiveDirectory__TrustedCertificatePath` | Optionaler Pfad zu einem privaten Root-CA- oder selbstsignierten Lab-Zertifikat für LDAPS | leer; Installer paketiert das Zertifikat als PEM |
| `ActiveDirectory__BindUsername` | UPN des Servicekontos | `svc-HybridDeviceTagger@bloedgelaber.de` |
| `ActiveDirectory__BindUserDistinguishedName` | DN des Servicekontos für `unicodePwd` | muss angepasst werden |
| `ActiveDirectory__PageSize` | LDAP-Seitengröße | `500` |
| `KeyVault__VaultUri` | URI des Key Vaults | muss angepasst werden |
| `KeyVault__CurrentPasswordSecretName` | Aktuell verwendetes Kennwort | `hybrid-device-tagger-ad-password-current` |
| `KeyVault__NextPasswordSecretName` | Staging- und Recovery-Kennwort | `hybrid-device-tagger-ad-password-next` |
| `PasswordRotation__RotateAfterDays` | Alter bei regulärer Rotation | `25` |
| `PasswordRotation__PasswordLength` | Länge des generierten Kennworts | `40` |
| `PasswordRotation__SecretExpiryDays` | Ablauf einer Secret-Version | `35` |
| `Tagging__Rules__<n>__Name` | Eindeutiger, case-insensitiver Regelname für Verwaltung und Logs | erforderlich |
| `Tagging__Rules__<n>__Description` | Lesbare Beschreibung des Zwecks | erforderlich |
| `Tagging__Rules__<n>__BaseDn` | OU-only-Pfad für alle Forest-Domains oder vollständiger domainspezifischer DN | erforderlich |
| `Tagging__Rules__<n>__ExtensionAttributes__extensionAttribute<m>` | Zielwert für Attribut 1 bis 15 | optional |
| `Tagging__Rules__<n>__EntraGroupIds__<m>` | Object ID einer statischen Entra-Sicherheitsgruppe | optional, maximal 50 |
| `Tagging__Rules__<n>__AdministrativeUnitIds__<m>` | Object ID einer normalen oder eingeschränkten administrativen Einheit | optional, maximal 50 |

Jede Regel benötigt mindestens eine Aktion aus diesen drei Zielarten. Die Mitgliedschaftsziele aller passenden Regeln werden vereinigt. HybridDeviceTagger speichert verwaltete Extension-Attribute mit ihrem zuletzt gesetzten Wert sowie ausschließlich selbst erzeugte Mitgliedschaften im privaten Blob-Container `membership-ledger`. Verlässt ein Computer alle konfigurierten Ziel-OUs oder entfällt eine Aktion aus allen passenden Regeln, entfernt die Function die noch unveränderten verwalteten Attribute und ihre eigenen Gruppen-, AU- und RMAU-Mitgliedschaften. Zwischenzeitlich manuell geänderte Attribute sowie bereits vorher manuell oder durch andere Systeme vorhandene Mitgliedschaften werden nicht entfernt.

Primär verwendet das Servicekonto Basic Bind über LDAPS. Der Installer prüft LDAPS zuerst. Bei erfolgreichem Bind wird `AllowLdapFallback` zwingend auf `false` gesetzt und LDAPS bleibt Pflicht. Ist LDAPS wegen eines Verbindungs-, TLS- oder Verfügbarkeitsfehlers nicht erreichbar, erklärt der Installer den Fallback und fragt, ob LDAP verwendet werden darf. Erst nach ausdrücklicher Zustimmung werden Geräteabfragen und Credential-Tests über LDAP mit Negotiate-Authentifizierung, LDAP Signing und Sealing ermöglicht. Ungültige Anmeldedaten lösen keinen Fallback aus. Der Fallback verwendet zwar kein TLS, schützt Bind und LDAP-Verkehr aber über die ausgehandelte SASL-Sicherheitsschicht.

Für private PKIs kann der Installer mit `-ActiveDirectoryTrustedCertificatePath` ein öffentliches Root-CA-Zertifikat im DER- oder PEM-Format übernehmen. Für einen Lab-DC darf dies auch dessen direkt selbstsigniertes Serverzertifikat sein. Das validierte Zertifikat wird ohne privaten Schlüssel als PEM in das Function-Paket geschrieben. Die Anwendung akzeptiert nur Zertifikate, die eine gültige Kette zu diesem Trust Anchor, die EKU Server Authentication und den DNS-Namen des LDAP-Ziels besitzen.

Beispiel für lokale App Settings:

```text
Tagging__Rules__0__Name=Tier 0 workstations
Tagging__Rules__0__Description=Tags privileged administrative workstations.
Tagging__Rules__0__BaseDn=OU=Tier0,OU=Admin
Tagging__Rules__0__ExtensionAttributes__extensionAttribute1=PAW
Tagging__Rules__0__ExtensionAttributes__extensionAttribute2=Tier0
Tagging__Rules__1__Name=Tier 1 servers
Tagging__Rules__1__Description=Tags server devices as Tier 1.
Tagging__Rules__1__BaseDn=OU=Server
Tagging__Rules__1__ExtensionAttributes__extensionAttribute2=Tier1
```

Enthält `BaseDn` nur OU-Komponenten, beispielsweise `OU=Tier0,OU=Admin`, liest die Function die Domain-CrossRefs aus der Forest-Konfigurationspartition und sucht diesen OU-Pfad in jeder Domain. Eine Domain, in der der Pfad nicht existiert, wird übersprungen; existiert er in keiner Domain, schlägt die Regel fehl. Ein vollständiger DN wie `OU=Tier0,OU=Admin,DC=child,DC=example,DC=com` begrenzt die Regel dagegen auf genau diese Domain.

Jede konkrete OU wird mit LDAP `SearchScope.Subtree` durchsucht und erfasst damit Computer direkt in der Basis-OU und in allen darunterliegenden OUs. Regeln dürfen sich überlappen; Zuweisungen unterschiedlicher Attribute werden für ein Gerät zusammengeführt. Weisen überlappende Regeln demselben Attribut unterschiedliche Werte zu, wird der Lauf mit einem Konfigurationsfehler beendet, statt einen reihenfolgeabhängigen Wert zu schreiben.

## Tagging-Regeln nach der Installation ändern

Mit `Deployment/Update-TaggingConfiguration.ps1` lässt sich der vollständige gewünschte Regelsatz einer bestehenden Function App aktualisieren, ohne Infrastruktur oder Anwendungscode erneut bereitzustellen. `-ShowCurrent` zeigt die aktuelle Konfiguration auch ohne `-TaggingRules` an. `-AddRule` akzeptiert eine Regel als PSObject oder JSON über `-InputObject`, ein einzelnes Pipelineobjekt oder die Parameter `RuleName`, `BaseDn`, `ExtensionAttribute` und `Value`; `RuleDescription` ist optional. Bei `-ShowCurrent`, `-AddRule` und `-RemoveRuleName` bleibt die lesbare Host-Anzeige erhalten und zusätzlich wird genau ein strukturiertes Konfigurationsobjekt ausgegeben. Bei `-WhatIf` enthält es den tatsächlich gespeicherten Iststand, nach einer erfolgreichen Änderung den neuen Stand. Das Skript validiert neue Regeln und schreibt sie atomar mit ETag-Schutz über die Entra-authentifizierte REST API in Blob Storage; ein Function-App-Neustart ist nicht erforderlich. Beispiele für Anzeigen, Hinzufügen, Ändern und Entfernen stehen in der [Installationsanleitung](Deployment/README.md#updating-tagging-rules-after-installation).

`-TaggingRules` ersetzt den vollständigen Regelsatz und verlangt deshalb zusätzlich zur normalen PowerShell-Bestätigung eine ausdrückliche Sicherheitsabfrage. `-Confirm:$false` umgeht diese Abfrage nicht. Nichtinteraktive, genehmigte Pipelines müssen `-ForceTaggingRulesReplacement` explizit angeben; `-WhatIf` benötigt keine Bestätigung.

Wird `-AddRule` interaktiv ohne `InputObject` und ohne vollständige Einzelparameter aufgerufen, fragt das Skript die fehlenden Werte direkt im Terminal ab. Die Aktionsauswahl `A`, `G` und `U` erlaubt Extension-Attribute, Gruppen, administrative Einheiten oder Kombinationen davon. Eine Regel kann mehrere Ziele jeder Art enthalten. Nichtinteraktiv werden korrespondierende Listen verwendet, beispielsweise `-ExtensionAttribute 1,2 -Value 'Server','Tier1' -EntraGroupId $groupId -AdministrativeUnitId $auId`. Attributnummern müssen eindeutig zwischen 1 und 15 liegen und beide Attributlisten gleich lang sein. In CI gibt es keine Prompts; dort müssen ein Inputobjekt oder alle Pflichtwerte übergeben werden.

Bei skalaren Parametern aktualisiert `-AddRule` einen bereits vorhandenen, case-insensitiven `RuleName`, statt eine doppelte Regel anzulegen. Ohne `-BaseDn` und `-RuleDescription` bleiben die vorhandenen Werte erhalten. Bestehende Extension-Attribute bleiben ebenfalls bestehen; angegebene Nummern werden ergänzt oder gezielt überschrieben. Nur ein neuer Regelname benötigt zwingend einen Base DN.

Mit `-EntraGroupId` und `-AdministrativeUnitId` lassen sich jeweils bis zu 50 eindeutige Object IDs setzen. Bei einem vorhandenen Regelname bleiben nicht angegebene Zielarten unverändert; eine ausdrücklich angegebene Liste ersetzt die bisherige Liste dieser Zielart. Eine leere Liste entfernt alle Ziele dieser Art und löst im nächsten Lauf nur für vom Tagger selbst erzeugte Mitgliedschaften eine Entfernung aus.

Regelnamen dürfen 1 bis 128 Unicode-Buchstaben oder Ziffern sowie Leerzeichen, Punkte, Unterstriche, Bindestriche und Klammern enthalten. Ein Base DN besteht aus gültigen `OU=`-Komponenten und optional einem nachfolgenden `DC=`-Teil; auch ein reiner Domain-DN ist zulässig. Sonderzeichen in DN-Werten müssen LDAP-konform maskiert werden. Deployment-Skripte und Function-Startup prüfen dieselben Regeln.

Eingabegrößen werden vor JSON-Verarbeitung sowie Azure- oder LDAP-Aufrufen begrenzt: maximal 100 Regeln und 32.768 Zeichen serialisiertes Regel-JSON. Pro Regel gelten 128 Zeichen für den Namen, 1.024 für die Beschreibung, 2.048 für den Base DN, höchstens 15 Extension-Attribute, 1.024 Zeichen je Attributwert sowie jeweils 50 eindeutige Gruppen- und AU-IDs. Ressourcen-, AD-, Key-Vault- und Tag-Parameter folgen zusätzlich den jeweiligen Dienstgrenzen und werden soweit relevant beim Function-Start erneut validiert.

Entfernte Regeln oder geänderte Tagwerte wirken nur auf künftige Synchronisierungsläufe. Bereits von einer alten Regel gesetzte Entra-Attribute werden nicht automatisch gelöscht.

## Management REST API

Die Function App stellt eine Single-Tenant-Entra-geschützte Verwaltungs-API unter `/api/management` bereit. App Service Easy Auth lehnt nicht authentifizierte Aufrufe mit HTTP `401` ab und akzeptiert nur die bei der Installation explizit angegebenen Benutzer- oder Service-Principal-Objekt-IDs. Zusätzlich validiert die Function den von Easy Auth erzeugten Principal Header. Es werden keine Function Keys oder Client Secrets benötigt.

| Methode und Pfad | Funktion |
| --- | --- |
| `GET /api/management/tagging-rules` | Aktuelle Regeln, ETag, Änderungszeit und Quelle lesen |
| `PUT /api/management/tagging-rules` | Den vollständigen validierten Regelsatz ersetzen |
| `PUT /api/management/tagging-rules/{ruleName}` | Eine vollständige Regel hinzufügen oder case-insensitiv ersetzen |
| `DELETE /api/management/tagging-rules/{ruleName}` | Eine Regel entfernen; mindestens eine Regel muss verbleiben |
| `GET /api/management/status/device-tagging` | Status und Ergebniszähler des letzten Tagging-Laufs lesen |
| `GET /api/management/status/password-rotation` | Status des letzten Checks und Zeitpunkt des letzten tatsächlichen Kennwortwechsels lesen |
| `GET /api/management/traces/device-tagging/latest` | Letzten JSONL-Trace als Download abrufen |

Die API speichert dynamische Regeln als `tagging-configuration.json` im privaten Container `run-traces`. Solange die API noch keine Änderung gespeichert hat, verwendet sie `Tagging__RulesJson` als Fallback. Jeder Tagging-Lauf liest zu Beginn genau einen aktuellen Snapshot; ein Function-Restart ist nach Änderungen nicht erforderlich. ETags verhindern, dass parallele Änderungen unbemerkt überschrieben werden. Die vollständige OpenAPI-3.0-Spezifikation steht in [docs/management-api.openapi.yaml](docs/management-api.openapi.yaml).

Für die drei häufigsten Leseoperationen steht `Deployment/Show-HybridDeviceTaggerApi.ps1` bereit. Das Skript verwendet ausschließlich die REST API und die bestehende Anmeldung von `az login`. Die produktive API-URL und ihre nicht geheime Client-ID sind als Standardwerte hinterlegt. Eine Subscription ID, Resource Group, ARM-Berechtigung oder weitere Eingabe ist nicht erforderlich:

```powershell
./Deployment/Show-HybridDeviceTaggerApi.ps1 -ShowRules
./Deployment/Show-HybridDeviceTaggerApi.ps1 -ShowStatus | Format-List
./Deployment/Show-HybridDeviceTaggerApi.ps1 -ShowTrace |
	Format-Table timestampUtc, level, phase, action, outcome
```

`ApiBaseUri` und `ManagementApiClientId` sind optionale Überschreibungen für eine andere Installation. Die Client-ID ist kein Kennwort oder Secret; Azure CLI benötigt sie intern nur als Audience des aus der bestehenden Anmeldung erzeugten Zugriffstokens.

Ein Bearer-Token muss als Audience die konfigurierte API-App-ID oder `api://<client-id>` besitzen:

```powershell
$headers = @{ Authorization = "Bearer $accessToken" }
Invoke-RestMethod `
	-Uri 'https://func-hybriddevicetagger.azurewebsites.net/api/management/status/device-tagging' `
	-Headers $headers

Invoke-WebRequest `
	-Uri 'https://func-hybriddevicetagger.azurewebsites.net/api/management/traces/device-tagging/latest' `
	-Headers $headers `
	-OutFile '.\device-tagger-last-run.jsonl'
```

## Erstmalige Einrichtung des AD-Kennworts

Vor dem ersten Start muss das bestehende Kennwort des Servicekontos einmalig als Secret `hybrid-device-tagger-ad-password-current` im Key Vault angelegt werden. `next` wird von der Rotation automatisch erstellt und darf nicht manuell als primäres Kennwort verwendet werden.

Die Managed Identity benötigt auf Secret-Ebene mindestens die Data Actions zum Lesen und Setzen von Secrets. `Key Vault Secrets User` reicht nicht aus, weil diese Rolle kein neues Secret setzen kann. Verwenden Sie eine eng begrenzte benutzerdefinierte Rolle oder, falls das organisatorisch nicht möglich ist, `Key Vault Secrets Officer`.

Das Laufzeitkonto bleibt reguläres Mitglied von `Domain Users`. Zusätzlich müssen auf dem Konto `SELF / Change Password` erlaubt und `User cannot change password` deaktiviert sein. Das Laufzeitkonto selbst benötigt weder administrative AD-Gruppen noch Reset-Password-Rechte; nur das getrennte Provisioning-Credential benötigt die Rechte zum Erstellen beziehungsweise Zurücksetzen.

Bei einer echten Installation kann der Installer das Servicekonto mit einem separaten AD-Provisioning-Credential vorbereiten. Fehlt das Konto, fragt er vor dem Erstellen und Aktivieren nach; ist es vorhanden, bietet er ein zufälliges komplexes Kennwort oder eine sichere manuelle Eingabe für den administrativen Reset an. Das Zielkennwort wird direkt als Current-Secret synchronisiert und nicht angezeigt. Der Installer bevorzugt LDAPS. Ist LDAPS nicht verfügbar und wurde der LDAP-Fallback ausdrücklich genehmigt, verwendet der Bootstrap das installierte Active-Directory-PowerShell-Modul über Active Directory Web Services gegen den konfigurierten Server. Nach Erstellung oder Reset bleibt **Benutzer muss Kennwort bei der nächsten Anmeldung ändern** deaktiviert. `-ValidateOnly` und `-WhatIf` verändern das AD-Konto nicht.

## Rotationsablauf

Die Rotation prüft das aktuelle Secret täglich und rotiert regulär ab einem Alter von 25 Tagen:

1. Neues zufälliges Kennwort als `next` in Key Vault speichern.
2. Eigenes AD-Kennwort über LDAPS mit einer kombinierten `unicodePwd`-Delete/Add-Operation ändern.
3. LDAP-Bind mit `next` validieren.
4. Das validierte Kennwort als neue Version von `current` speichern.

Wenn `current` nach einem unterbrochenen Lauf nicht mehr funktioniert, testet die Function zuerst `next` und übernimmt es bei Erfolg als `current`. Auch die Geräteabfrage verwendet diesen Fallback.

Kennwortänderungen über `unicodePwd` und die anschließende Validierung bleiben immer LDAPS-pflichtig. Ist eine Rotation fällig, aber LDAPS nicht verfügbar, wird sie mit `DeferredNoSecureConnection` ohne neues `next`-Secret und ohne Änderung in AD zurückgestellt. Der nächste geplante Lauf versucht die Rotation erneut.

## Lokal ausführen

1. `src/HybridDeviceTagger/local.settings.example.json` nach `src/HybridDeviceTagger/local.settings.json` übertragen und Domain Controller, Benutzer-DN sowie Vault-URI anpassen.
2. Azurite starten oder `AzureWebJobsStorage` auf eine Entwicklungs-Storage-Verbindung setzen.
3. Das bestehende AD-Kennwort als `current`-Secret im Key Vault anlegen.
4. Eine lokale Azure-Anmeldung mit Secret-Lese- und Schreibrechten bereitstellen, die über `DefaultAzureCredential` verwendet werden kann.
5. In `src/HybridDeviceTagger` den Befehl `func start` ausführen.

`local.settings.json` ist von Git ausgeschlossen. Zugangsdaten dürfen nicht in die Beispielkonfiguration oder das Repository eingetragen werden.

## Build und Tests

```powershell
dotnet build HybridDeviceTagger.sln --configuration Release
dotnet test HybridDeviceTagger.sln --configuration Release
```

Die Unit-Tests prüfen Geräteabgleich, Fälligkeit, reguläre Rotation, Recovery und Kennwortzusammensetzung.

## Versionierung und Autor

Das Projekt verwendet die Produktversion `<major>.<minor>.<yyyyMMdd>.<counter>`, aktuell `0.1.20260905.7`. Der Zähler beginnt für jede Veröffentlichung am selben Datum bei `1` und wird bei weiteren Veröffentlichungen an diesem Tag erhöht. Die zentrale Definition steht in `Directory.Build.props` und wird als Paket- und Informational-Version in die Build-Ausgabe übernommen.

Vor jedem Commit müssen die Version in `Directory.Build.props` erhöht und ein datierter Abschnitt für exakt diese Version in [CHANGELOG.md](CHANGELOG.md) ergänzt werden. Das eingecheckte Prüfskript validiert beide Bedingungen gegen `HEAD`. Der Repository-Hook wird einmalig pro Clone aktiviert:

```powershell
git config core.hooksPath .githooks
```

Nach dem Staging kann dieselbe Prüfung manuell ausgeführt werden:

```powershell
pwsh -NoProfile -File scripts/Assert-CommitMetadata.ps1 -Staged
```

Da CLR-Assembly- und Dateiversionen nur Zahlenwerte bis `65534` je Segment unterstützen, bleiben diese separat kompatibel: `AssemblyVersion` ist `0.1.0.0`, `FileVersion` ist `0.1.0.2`. Ein Build- oder Releaseprozess kann die Produktversion ohne Dateiänderung setzen:

```powershell
dotnet build HybridDeviceTagger.sln --configuration Release `
	-p:VersionMajor=0 `
	-p:VersionMinor=1 `
	-p:VersionDate=20260904 `
	-p:VersionCounter=2
```

Autor und Ansprechpartner ist Andreas Lucas (aka Kili), `andreas.lucas@microsoft.com`.

## Installation in Azure

Das Verzeichnis `Deployment` enthält ein idempotentes PowerShell-Installationsskript und die Bicep-Vorlagen für Azure Functions Flex Consumption (FC1), Storage, Log Analytics und Application Insights. VNet, Integrationssubnetz und Key Vault werden als vorhandene Ressourcen geprüft und referenziert, aber nicht erstellt oder verändert.

Beginnen Sie immer mit `-ValidateOnly`, danach mit `-WhatIf`, und führen Sie erst anschließend die Installation aus. Parameter, Voraussetzungen, Berechtigungen und ein vollständiges Beispiel stehen in [Deployment/README.md](Deployment/README.md).

## Code-Dokumentation

Eine englische Beschreibung der Architektur, Abläufe sowie der Verantwortlichkeiten und Zusammenarbeit aller Klassen steht in [CODE_DOCUMENTATION.md](CODE_DOCUMENTATION.md).

## Azure-Betrieb

Die Function App muss aus ihrem virtuellen Netzwerk den konfigurierten Domain Controller sowie die über die Forest-CrossRefs ermittelten Domain-DNS-Namen auflösen und per LDAPS erreichen, beispielsweise über Site-to-Site-VPN oder ExpressRoute. Optional kann LDAP auf Port 389 als Fallback erreichbar sein. Legen Sie alle Werte aus der Konfiguration als Function-App-Settings an und aktivieren Sie Managed Identity. Weisen Sie deren Service Principal die drei beschriebenen Graph-App-Rollen sowie die Key-Vault- und Blob-Rechte zu. Für Geräte in eingeschränkten administrativen Einheiten benötigt die Managed Identity zusätzlich die vom Installer erzeugte Least-Privilege-Rolle direkt am jeweiligen RMAU-Scope.

### Ablauf- und Fehlerprotokolle

Die Anwendung sendet strukturierte OpenTelemetry-Logs, Exceptions sowie HTTP-Abhängigkeiten an das mit der Function App verbundene Application Insights. In Azure öffnen Sie `func-hybriddevicetagger-appi` und anschließend **Überwachung > Protokolle**. Jede Timer-Ausführung schreibt Start, Ergebnis oder Fehler mit derselben `InvocationId`. Geräteupdates enthalten nur die Device-ID und die Namen geänderter Attribute; Attributwerte und Kennwörter werden nicht protokolliert.

Bei direkter Abfrage des verbundenen Log-Analytics-Workspace `func-hybriddevicetagger-law` heißen die entsprechenden Tabellen `AppTraces`, `AppExceptions`, `AppRequests` und `AppDependencies`. Die folgenden Kurzformen `traces`, `exceptions`, `requests` und `dependencies` gelten im Protokollbereich der Application-Insights-Ressource.

Letzte Abläufe und ihre Ergebnisfelder:

```kusto
traces
| where timestamp > ago(24h)
| where cloud_RoleName == "func-hybriddevicetagger"
| where message has_any ("Device tagging", "Tagging rule", "Password rotation")
| project timestamp, severityLevel, message, operation_Id, customDimensions
| order by timestamp desc
```

Fehler einschließlich Stacktrace:

```kusto
exceptions
| where timestamp > ago(7d)
| where cloud_RoleName == "func-hybriddevicetagger"
| project timestamp, type, outerMessage, innermostMessage, operation_Id, details
| order by timestamp desc
```

Fehlgeschlagene Aufrufe an Microsoft Graph oder Key Vault:

```kusto
dependencies
| where timestamp > ago(7d)
| where cloud_RoleName == "func-hybriddevicetagger" and success == false
| project timestamp, target, name, resultCode, duration, operation_Id
| order by timestamp desc
```

Mit einer `operation_Id` aus einem Treffer lässt sich eine komplette Ausführung korrelieren:

```kusto
union traces, exceptions, requests, dependencies
| where operation_Id == "OPERATION_ID_EINSETZEN"
| order by timestamp asc
```

Die Funktion markiert Geräte innerhalb der Ziel-OUs. Verlässt ein Computer alle konfigurierten Ziel-OUs, entfernt der nächste Lauf die noch unveränderten verwalteten Extension Attributes sowie die vom Tagger angelegten Gruppen-, AU- und RMAU-Mitgliedschaften.

## Troubleshooting

### Installationsprotokoll

Jeder Aufruf von `Install-HybridDeviceTagger.ps1` ersetzt die Datei `HybridDeviceTagger-Install.log` im temporären Verzeichnis des aktuellen Benutzers. Das PowerShell-Transcript enthält die Konsolenausgaben aller Validierungs-, What-if-, AD-Vorbereitungs- und Installationsschritte und wird auch bei einem Fehler oder frühen Abbruch sauber geschlossen.

```powershell
$installerLog = Join-Path ([System.IO.Path]::GetTempPath()) 'HybridDeviceTagger-Install.log'
code $installerLog
```

Das Transcript kann Benutzer-, Geräte-, Ressourcen- und Verzeichnisnamen enthalten und muss daher vertraulich behandelt werden. Kennwörter werden vom Skript nicht ausgegeben und dürfen auch bei manueller Fehleranalyse nicht in die Konsole geschrieben werden.

### Trace des letzten Tagging-Laufs

Jeder Start von `DeviceTaggerFunction` ersetzt die Datei `device-tagger-last-run.jsonl` im privaten Blob-Container `run-traces`. Jeder Arbeitsschritt wird sofort als einzelne JSON-Zeile geschrieben. Dadurch bleibt bei einer normalen Exception oder einem Prozessabbruch der bis dahin geschriebene Ablauf erhalten. Die Datei enthält unter anderem UTC-Zeit, laufende Sequenznummer, Invocation-ID, Phase, Aktion, Ergebnis, Device- und Ziel-IDs sowie bei Fehlern Exception-Typ, Meldung und Stacktrace. Kennwörter, Tokens und Extension-Attributwerte werden nicht gespeichert; für Attributvergleiche enthält der Trace nur SHA-256-Fingerprints.

Die Datei ist bewusst als JSON Lines aufgebaut. Copilot kann einzelne Zeilen unabhängig analysieren, nach `invocationId`, `phase`, `action`, `outcome`, `deviceId` oder `targetId` filtern und den letzten erfolgreichen Schritt unmittelbar vor einem Fehler bestimmen. Da Device- und Entra-Objekt-IDs sowie Infrastrukturpfade enthalten sind, muss die Datei trotz fehlender Geheimnisse vertraulich behandelt werden.

### Trace exportieren und in VS Code öffnen

Zum Herunterladen benötigt das angemeldete Konto `Storage Blob Data Reader` oder eine höherwertige Datenrolle und Netzwerkzugriff auf den privaten Blob-Endpunkt:

```powershell
az storage blob download `
	--account-name '<storage-account>' `
	--container-name 'run-traces' `
	--name 'device-tagger-last-run.jsonl' `
	--file '.\device-tagger-last-run.jsonl' `
	--auth-mode login

code '.\device-tagger-last-run.jsonl'
```

Der exportierte Trace kann anschließend direkt in VS Code geöffnet und Copilot zur Fehleranalyse übergeben werden.