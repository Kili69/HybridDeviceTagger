<p align="center">
  <img src="assets/kili-logo.png" alt="Kili-Logo" width="220">
</p>

# HybridDeviceTagger

[![Buy me a coffee – Kili](https://img.shields.io/badge/Buy_me_a_coffee-Kili-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=000000)](https://buymeacoffee.com/andreaslmuz)
[![Version](https://img.shields.io/badge/Version-0.1.20261005.3-2EA44F?style=for-the-badge)](CHANGELOG.md)
[![Branch](https://img.shields.io/badge/Branch-dev-181717?style=for-the-badge&logo=git&logoColor=white)](https://github.com/Kili69/HybridDeviceTagger/tree/dev)
[![Lizenz: Apache 2.0](https://img.shields.io/badge/Lizenz-Apache_2.0-0B5CAD?style=for-the-badge)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Azure Functions v4](https://img.shields.io/badge/Azure_Functions-v4-0062AD?style=for-the-badge&logo=azurefunctions&logoColor=white)

Für die Umsetzung einer Zero-Trust-Strategie muss nicht nur die Identität eines Benutzers, sondern auch das verwendete Quellsystem zuverlässig bestimmt und bewertet werden. In Conditional-Access-Richtlinien lässt sich diese Einordnung bei Geräten üblicherweise über Extension Attributes abbilden. Damit kann beispielsweise geprüft werden, ob ein Quellsystem einer definierten Sicherheitsstufe entspricht.

Voraussetzung dafür ist ein in Microsoft Entra ID verfügbares Geräteobjekt, beispielsweise ein Microsoft-Entra-hybrid-joined Computer. Eine Bewertung allein anhand der Quelladresse reicht häufig nicht aus: Conditional Access sieht in der Regel nur die öffentliche IP-Adresse des Zugriffs. Befinden sich mehrere Systeme hinter derselben öffentlichen Adresse, lässt sich daraus weder das konkrete Quellsystem noch dessen Sicherheitsklassifizierung ableiten.

Im lokalen Active Directory kann diese Klassifizierung über die Verzeichnisstruktur und die Organisationseinheit eines Computerobjekts abgebildet werden. Beim Synchronisieren von Computerobjekten nach Microsoft Entra ID geht diese Information jedoch verloren, weil der Distinguished Name (DN) und damit die OU-Zuordnung nicht übernommen werden.

Genau hier setzt HybridDeviceTagger an. Er wertet die Position eines Computerobjekts im Active Directory aus, wendet darauf konfigurierbare Regeln an und überträgt die daraus abgeleitete Sicherheitsklassifizierung nach Microsoft Entra ID. Abhängig von der AD-OU kann er:

- `extensionAttribute1` bis `extensionAttribute15` setzen,
- das Gerät Entra-Sicherheitsgruppen hinzufügen und
- das Gerät normalen oder eingeschränkten administrativen Einheiten zuordnen.

## Inhaltsverzeichnis

- [Funktionsweise](#funktionsweise)
- [Installation](#installation)
  - [Skriptbasierte Installation](#skriptbasierte-installation)
  - [Pipeline-Installation](#pipeline-installation)
  - [Gemeinsame Konfiguration](#gemeinsame-konfiguration)
- [Administration](#administration)
- [Azure-Betrieb](#azure-betrieb)
- [Problembehandlung](#problembehandlung)
- [Entwicklerdokumentation](#entwicklerdokumentation)
- [Lizenz](#lizenz)

## Funktionsweise

HybridDeviceTagger durchsucht die konfigurierten OUs im Active Directory über LDAP. Für jedes gefundene Computerobjekt prüft er, ob ein zugehöriges Geräteobjekt in Entra ID vorhanden ist. Bei einer Übereinstimmung werden die für die jeweilige OU konfigurierten Extension-Attribute sowie Mitgliedschaften in Entra-Sicherheitsgruppen und administrativen Einheiten angewendet.

```mermaid
flowchart LR
	A[AD-OUs über LDAP durchsuchen] --> B[Computerobjekte erkennen]
	B --> C{Passendes Gerät<br/>in Entra ID?}
	C -->|Nein| D[Gerät überspringen]
	C -->|Ja| E[Extension-Attribute setzen]
	E --> F[Gruppenmitgliedschaften zuweisen]
	F --> G[Administrativen Einheiten zuweisen]
```

## Installation

HybridDeviceTagger kann interaktiv mit dem bereitgestellten PowerShell-Skript oder unbeaufsichtigt aus einer beliebigen CI/CD-Pipeline installiert werden. Beide Varianten verwenden denselben Installer und dieselben Bicep-Vorlagen. Dadurch entstehen unabhängig vom Ausführungsweg identische Azure-Ressourcen und Anwendungseinstellungen.

### Skriptbasierte Installation

Die skriptbasierte Installation eignet sich für die Erstbereitstellung, Testumgebungen und manuell kontrollierte Änderungen. Der Installer fragt fehlende Werte interaktiv ab, validiert die Umgebung und zeigt vor Änderungen eine Zusammenfassung.

#### Voraussetzungen

- PowerShell 7.2 oder neuer mit dem ActiveDirectory-Modul
- Azure CLI 2.60.0 oder neuer
- Eine Azure Subscription, in der der Installationsbenutzer Ressourcen bereitstellen, das vorhandene virtuelle Netzwerk einbinden und RBAC-Rollenzuweisungen erstellen darf
- DNS-Auflösung und Netzwerkzugriff der Function App auf die DNS-Namen aller betroffenen Forest-Domains über LDAPS auf Port 636 oder ersatzweise LDAP auf Port 389
- Netzwerkzugriff der Function App und des Installationshosts auf die in CA-signierten LDAPS-Zertifikaten veröffentlichten CRL- oder OCSP-Endpunkte; ein nicht bestätigbarer Sperrstatus wird abgelehnt
- Ein vorhandenes virtuelles Netzwerk mit Subnetzen für die Function-Integration und den privaten Storage-Endpunkt
- Ein forestweit verwendbares AD-Servicekonto mit Leserechten auf die konfigurierten OUs in allen Domains und dem Recht, das eigene Kennwort zu ändern
- Die in der [Installationsanleitung](Deployment/README.md#required-permissions) beschriebenen Azure-, Entra- und Active-Directory-Berechtigungen

#### Installation ausführen

Nach dem Entpacken des Installationspakets zunächst bei Azure anmelden:

```powershell
az login
```

Anschließend den Installer interaktiv in drei Schritten ausführen. Er fragt alle benötigten Werte ab und schlägt passende Standardwerte vor:

- **Azure-Ziel:** Subscription, Ressourcengruppe, Region sowie Namen für Function App und Storage Account
- **Netzwerk:** Subscription und Ressourcengruppe des vorhandenen VNet, VNet-Name, Function-Integrationssubnetz und separates Private-Endpoint-Subnetz
- **Key Vault:** Auswahl zwischen einem vorhandenen Vault und einem neuen privaten Vault; bei einem vorhandenen Vault zusätzlich Subscription, Ressourcengruppe und Name
- **Active Directory:** Forest-DNS-Name, optional ein bestimmter Domain Controller, UPN und Distinguished Name des Servicekontos sowie dessen Kennwort über einen geschützten Anmeldedialog
- **Erste Tagging-Regel:** Name, Beschreibung, Basis-DN der OU und Wert für `extensionAttribute1`
- **Management API:** Client-ID der vorhandenen Entra-App-Registrierung und die Objekt-ID des berechtigten Benutzers, der Gruppe oder des Service Principals

Ist LDAPS nicht verfügbar, fragt der Installer vor der Verwendung des abgesicherten LDAP-Fallbacks nach einer Bestätigung. Regeln mit eingeschränkten administrativen Einheiten erfordern ebenfalls eine ausdrückliche Freigabe. Vor der eigentlichen Installation zeigt der Installer eine Zusammenfassung und fordert eine abschließende Bestätigung an.

```powershell
./Deployment/Install-HybridDeviceTagger.ps1 -ValidateOnly
./Deployment/Install-HybridDeviceTagger.ps1 -WhatIf
./Deployment/Install-HybridDeviceTagger.ps1
```

`-ValidateOnly` prüft die Voraussetzungen ohne Änderungen. `-WhatIf` zeigt die geplanten Azure-Änderungen. Erst der dritte Aufruf führt die Installation nach Bestätigung aus.

Bei jedem der drei interaktiven Aufrufe werden die Angaben erneut abgefragt. Für eine wiederholbare Ausführung sollten die Werte deshalb in einem PowerShell-Splat vorbereitet werden.

#### Wiederholbare Skriptausführung

Alle Werte können als Parameter oder PowerShell-Splat übergeben und für Validierung, What-if und Installation wiederverwendet werden:

```powershell
$parameters = @{
    SubscriptionId = '<Azure-Subscription-ID>'
    ResourceGroupName = 'rg-hybrid-device-tagger'
    Location = 'westeurope'
    FunctionAppName = 'func-hybrid-device-tagger'
    StorageAccountName = 'sthybriddevicetagger'
    # Weitere Netzwerk-, AD-, Key-Vault- und Management-API-Parameter
}

./Deployment/Install-HybridDeviceTagger.ps1 @parameters -ValidateOnly
./Deployment/Install-HybridDeviceTagger.ps1 @parameters -WhatIf
./Deployment/Install-HybridDeviceTagger.ps1 @parameters
```

Mit `-CreateKeyVault` wird ein neuer privater Key Vault in der Ziel-Ressourcengruppe erstellt; ohne diesen Schalter wird ein vorhandener Key Vault verwendet. `-CreateResourceGroup`, `-RegisterProviders`, `-ActiveDirectoryTrustedCertificatePath` und die rollenbezogenen Management-API-Parameter steuern weitere Installationsoptionen. Ein vollständiges Parameterbeispiel steht in der [erweiterten Installationsanleitung](Deployment/README.md#validation).

### Pipeline-Installation

Die Pipeline-Installation verwendet denselben PowerShell-Installer im vollständig parametrisierten Noninteractive-Modus. Sie ist nicht an GitHub Actions, Azure DevOps oder ein anderes Pipeline-System gebunden.

#### Voraussetzungen

- Ein Windows-basierter, selbst gehosteter Runner mit PowerShell 7.2, Azure CLI, ActiveDirectory-Modul und Netzwerkzugriff auf Active Directory
- .NET 8 SDK, wenn das Installationspaket innerhalb der Pipeline erzeugt wird
- Eine nicht interaktive Azure-Anmeldung, vorzugsweise über Workload Identity Federation; alternativ kann ein Service Principal verwendet werden
- Dieselben Azure-, Entra-, Netzwerk- und Active-Directory-Berechtigungen wie bei der skriptbasierten Installation
- Geschützte Pipeline-Secrets für das AD-Servicekonto und gegebenenfalls das separate AD-Provisioning-Credential
- Vollständig gesetzte Installer-Parameter; die Pipeline darf nicht von interaktiven Eingabeaufforderungen abhängen

Ein von einem Pipeline-Anbieter gehosteter Runner kann nur verwendet werden, wenn er die privaten DNS-Zonen und Domain Controller sicher erreichen kann. Für typische Hybridumgebungen ist daher ein selbst gehosteter Runner im verbundenen Netzwerk erforderlich.

#### Pipeline-Ablauf

Eine plattformneutrale Pipeline sollte die folgenden Stufen verwenden:

1. **Build und Tests:** Solution im Release-Modus bauen und testen.
2. **Paket erstellen:** Mit `scripts/New-InstallationPackage.ps1` das vollständige Installationsarchiv erzeugen und als Pipeline-Artefakt veröffentlichen.
3. **Authentifizieren:** Azure CLI noninteractive mit der Pipeline-Identität anmelden.
4. **Validieren:** Installer mit allen Parametern, `-ValidateOnly` und `-Confirm:$false` ausführen.
5. **What-if:** Denselben Parametersatz mit `-WhatIf` und `-Confirm:$false` ausführen.
6. **Bereitstellen:** Nach einer optionalen Freigabe denselben Parametersatz mit `-Confirm:$false` ausführen.

```powershell
# Build-Stufe
dotnet test HybridDeviceTagger.sln --configuration Release
$package = pwsh -NoProfile -File scripts/New-InstallationPackage.ps1 `
    -OutputDirectory artifacts

# Deployment-Stufe nach dem Entpacken des Pipeline-Artefakts
$parameters = @{
    SubscriptionId = $env:HDT_SUBSCRIPTION_ID
    ResourceGroupName = $env:HDT_RESOURCE_GROUP
    # Alle weiteren nicht vertraulichen Installerwerte aus Pipelinevariablen
}

$securePassword = ConvertTo-SecureString $env:HDT_AD_PASSWORD -AsPlainText -Force
$parameters.ActiveDirectoryCredential = [pscredential]::new(
    $env:HDT_AD_USERNAME,
    $securePassword
)

./Deployment/Install-HybridDeviceTagger.ps1 @parameters -ValidateOnly -Confirm:$false
./Deployment/Install-HybridDeviceTagger.ps1 @parameters -WhatIf -Confirm:$false
./Deployment/Install-HybridDeviceTagger.ps1 @parameters -Confirm:$false
```

Pipeline-Secrets dürfen weder in Parameterdateien eingecheckt noch in Logs ausgegeben werden. Wenn der LDAP-Fallback in einer Noninteractive-Ausführung ausdrücklich erlaubt werden soll, muss `AllowLdapFallback = $true` im Parametersatz gesetzt werden. Mit `-SkipCodeDeployment` können Infrastruktur- und Anwendungsdeployment in getrennte Pipeline-Stufen aufgeteilt werden.

### Gemeinsame Konfiguration

Beide Installationsarten verwenden dieselben Parameter und erzeugen dieselbe Zielkonfiguration.

#### Key Vault

Ein bestehender Key Vault ist optional. Ist keiner vorhanden, erstellt das Setup mit `-CreateKeyVault` einen neuen privaten Key Vault in der Ressourcengruppe der Function App. Ohne diesen Schalter wird ein vorhandener Vault über Subscription, Ressourcengruppe und Namen referenziert.

Das AD-Kennwort wird nicht durch die Installationsidentität in den Key Vault geschrieben. Der Installer übergibt es an die Entra-geschützte Management API; die Function validiert es und speichert es anschließend mit ihrer Managed Identity.

#### Berechtigungen für Regel- und Kennwortverwaltung

Regel- und Kennwortverwaltung werden durch Anwendungsrollen der Entra Enterprise Application für die HybridDeviceTagger Management API geschützt. Zusätzlich erhalten alle Principals mit der Azure-RBAC-Rolle `Owner` oder `Contributor` auf der Function App automatisch dieselben Rechte wie `HybridDeviceTagger.RuleAdministrator` und `HybridDeviceTagger.PasswordRotationAdministrator`. Das gilt auch für über Sicherheitsgruppen oder übergeordnete Scopes geerbte Zuweisungen.

| Verwaltungsaufgabe | Erforderliche Anwendungsrolle | Installer-Parameter |
| --- | --- | --- |
| Tagging-Regeln lesen, erstellen, ändern und löschen | `HybridDeviceTagger.RuleAdministrator` | `ManagementApiRuleAdministratorPrincipalObjectId` |
| Initiales AD-Kennwort einrichten, Rotationsstatus lesen und Kennwortrotation erzwingen | `HybridDeviceTagger.PasswordRotationAdministrator` | `ManagementApiPasswordRotationAdministratorPrincipalObjectId` |

Die Parameter akzeptieren die Entra Object IDs von Benutzern, Sicherheitsgruppen oder Service Principals. Für die dauerhafte Administration sollten Sicherheitsgruppen verwendet und nach dem Least-Privilege-Prinzip nur der jeweils benötigten Rolle zugewiesen werden. Alternativ weist `ManagementApiAllowedPrincipalObjectId` einem Bootstrap-Principal alle fünf Management-API-Rollen zu. Diese breite Zuweisung eignet sich nur für Migration oder Ersteinrichtung.

Kennwortadministratoren benötigen keinen direkten Key-Vault-Zugriff. Nach einer neuen API-Anwendungsrollenzuweisung muss das Zugriffstoken erneuert werden. Änderungen an Owner-/Contributor-Zuweisungen prüft die Function bei der nächsten API-Anfrage über Azure Resource Manager. Andere Azure-RBAC-Rollen berechtigen nicht zur Management API; Owner und Contributor erhalten keine Tagging-Operator- oder Zertifikatsadministratorrechte.

#### Active Directory und Tagging-Regeln

Beide Installationsarten verwenden dasselbe AD-Servicekonto, dieselben Verbindungs- und Zertifikatseinstellungen sowie dieselben Tagging-Regeln. Das Servicekonto muss forestweit die benötigten Computerobjekte lesen und sein eigenes Kennwort ändern dürfen. Kennwortänderungen erfolgen ausschließlich über LDAPS.

Tagging-Regeln können bereits als `TaggingRules` an den Installer übergeben oder nach der Installation über das PowerShell-Management-Modul gepflegt werden. Das initiale AD-Kennwort kann ebenfalls während der Installation oder anschließend mit `Initialize-HybridDeviceTaggerActiveDirectoryCredential` gesetzt werden. Die folgenden Abschnitte beschreiben die gemeinsame Administration nach beiden Installationsarten.

#### PowerShell-Management-Modul nachinstallieren

Das PowerShell-Modul ist nicht für den Betrieb der Azure Function erforderlich. Es wird auf
Administrationsarbeitsplätzen oder Pipeline-Runnern installiert, von denen aus Regeln,
LDAPS-Vertrauenszertifikate, Statusinformationen und administrative Aktionen verwaltet werden sollen.

Sobald die erste Version auf PSGallery veröffentlicht wurde, kann das Modul unter PowerShell 7.2 oder
neuer für den aktuellen Benutzer installiert werden:

```powershell
Install-PSResource `
    -Name HybridDeviceTagger.Management `
    -Repository PSGallery `
    -Scope CurrentUser
```

Ist `Install-PSResource` auf dem System noch nicht verfügbar, kann alternativ PowerShellGet verwendet
werden:

```powershell
Install-Module `
    -Name HybridDeviceTagger.Management `
    -Repository PSGallery `
    -Scope CurrentUser
```

Eine bereits installierte Version wird mit dem zum Installationsweg passenden Befehl aktualisiert:

```powershell
Update-PSResource -Name HybridDeviceTagger.Management

# Alternative für eine Installation mit Install-Module
Update-Module -Name HybridDeviceTagger.Management
```

Anschließend das Modul importieren, die installierte Version prüfen und mit der Management API
verbinden:

```powershell
Import-Module HybridDeviceTagger.Management

Get-Module HybridDeviceTagger.Management -ListAvailable |
    Sort-Object Version -Descending |
    Select-Object -First 1 Name, Version, ModuleBase

az login
Connect-HybridDeviceTagger
Get-HybridDeviceTaggerRule
```

Für eine abweichende Bereitstellung werden deren API-URI und Entra-Client-ID angegeben:

```powershell
Connect-HybridDeviceTagger `
    -ApiBaseUri 'https://example.azurewebsites.net/api/management' `
    -ClientId '00000000-0000-0000-0000-000000000000'
```

Der angemeldete Benutzer benötigt die für die jeweilige Verwaltungsaufgabe dokumentierte
Management-API-Anwendungsrolle oder eine wirksame Owner-/Contributor-Zuweisung auf der Function App.
In abgeschotteten Umgebungen kann weiterhin das mit dem Installationspaket ausgelieferte Modul über
`Import-Module ./Deployment/HybridDeviceTagger.Management` geladen werden.

Alle Parameter, Berechtigungen und vollständigen Beispiele stehen in [Deployment/README.md](Deployment/README.md).

## Administration

### Tagging-Regeln verwalten

Tagging-Regeln werden mit dem PowerShell-Modul `Deployment/HybridDeviceTagger.Management` verwaltet. Zuerst das Modul importieren und mit der Management API verbinden:

```powershell
Import-Module ./Deployment/HybridDeviceTagger.Management
Connect-HybridDeviceTagger
```

Eine Regel besteht aus folgenden Eigenschaften:

| Eigenschaft | Beschreibung |
| --- | --- |
| `name` | Eindeutiger, nicht zwischen Groß- und Kleinschreibung unterscheidender Regelname |
| `description` | Verständliche Beschreibung des Regelzwecks |
| `baseDn` | OU-Pfad für alle Forest-Domains oder vollständiger DN einer Domain des konfigurierten AD Forests |
| `extensionAttributes` | Zu setzende Werte für `extensionAttribute1` bis `extensionAttribute15` |
| `entraGroupIds` | Object IDs statischer Entra-Sicherheitsgruppen |
| `administrativeUnitIds` | Object IDs normaler oder eingeschränkter administrativer Einheiten |

Jede Regel benötigt mindestens eine Aktion in `extensionAttributes`, `entraGroupIds` oder `administrativeUnitIds`. Nicht verwendete Zielarten werden als leere Hashtable beziehungsweise leeres Array angegeben.

Vor dem Speichern einer vollständigen Domain-DN-Regel liest die Function die Domain-CrossRefs aus der
Forest-Konfigurationspartition über den fest konfigurierten Domain Controller. Die aus `DC=` abgeleitete
DNS-Domain muss exakt einem dort veröffentlichten `dnsRoot` entsprechen. Fremde, ähnlich benannte oder
nur als Suffix passende Domains werden abgelehnt. OU-Pfade ohne `DC=` sind ebenfalls sicher begrenzt,
weil die Function ihre LDAP-Ziele ausschließlich aus den ermittelten Forest-Domains erzeugt. Dieselbe
Prüfung wird vor jedem Tagging-Lauf erneut durchgeführt, sodass auch ältere gespeicherte Regeln oder
eine Startkonfiguration keinen LDAP-Bind zu einem Ziel außerhalb des Forests auslösen können.

#### Regeln anzeigen

```powershell
# Alle Regeln anzeigen
Get-HybridDeviceTaggerRule

# Eine bestimmte Regel anzeigen
Get-HybridDeviceTaggerRule -Name 'Kiosk devices'
```

#### Regel erstellen

Eine neue Regel wird als vollständige Hashtable angelegt. Mit `-WhatIf` lässt sich die Änderung vorab prüfen:

```powershell
$rule = @{
    name = 'Kiosk devices'
    description = 'Tags devices located in the kiosk OU.'
    baseDn = 'OU=Kiosks,OU=Workstations'
    extensionAttributes = @{
        extensionAttribute1 = 'Kiosk'
    }
    entraGroupIds = @()
    administrativeUnitIds = @()
}

Add-HybridDeviceTaggerRule -Rule $rule -WhatIf
Add-HybridDeviceTaggerRule -Rule $rule
```

#### Regel ändern

Zum Ändern wird die Regel vollständig mit demselben Namen erneut übergeben. Der Namensvergleich erfolgt ohne Beachtung der Groß- und Kleinschreibung; alle anderen Regeln bleiben unverändert:

```powershell
$updatedRule = @{
    name = 'Kiosk devices'
    description = 'Tags shared kiosk devices and assigns their security group.'
    baseDn = 'OU=Kiosks,OU=Workstations'
    extensionAttributes = @{
        extensionAttribute1 = 'Kiosk'
        extensionAttribute2 = 'Shared'
    }
    entraGroupIds = @(
        '<Entra-Gruppen-Object-ID>'
    )
    administrativeUnitIds = @()
}

Add-HybridDeviceTaggerRule -Rule $updatedRule -WhatIf
Add-HybridDeviceTaggerRule -Rule $updatedRule
```

#### Regel löschen

Eine Regel wird über ihren Namen entfernt. Der letzte verbliebene Regeleintrag kann nicht gelöscht werden:

```powershell
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -WhatIf
Remove-HybridDeviceTaggerRule -Name 'Kiosk devices' -Confirm
```

Änderungen werden ohne Neustart der Function App wirksam. ETags schützen parallele Änderungen vor unbemerktem Überschreiben. Eine Konfiguration unterstützt maximal 100 Regeln; pro Regel sind bis zu 15 Extension Attributes und jeweils 50 eindeutige Gruppen- oder Administrative-Unit-IDs zulässig. Weitere Beispiele stehen in der [Installationsanleitung](Deployment/README.md#updating-tagging-rules-after-installation) und in der integrierten Hilfe:

```powershell
Get-Help Add-HybridDeviceTaggerRule -Full
Get-Help Remove-HybridDeviceTaggerRule -Full
```

### Erstmalige Einrichtung des AD-Kennworts

Das initiale Kennwort wird über die Entra-geschützte Function API eingerichtet. Der ausführende Benutzer benötigt dadurch keine Datenberechtigung auf dem Key Vault, sondern nur die API-Rolle `HybridDeviceTagger.PasswordRotationAdministrator`.

```powershell
Import-Module ./Deployment/HybridDeviceTagger.Management
Connect-HybridDeviceTagger
$password = Read-Host 'AD-Servicekonto-Kennwort' -AsSecureString
Initialize-HybridDeviceTaggerActiveDirectoryCredential -Password $password -Confirm
```

Die Function validiert das Kennwort durch einen AD-Bind und speichert es anschließend mit ihrer Managed Identity als Secret `hybrid-device-tagger-ad-password-current`. Ein bereits vorhandenes Current-Secret wird nicht überschrieben. `next` wird ausschließlich von der Rotation erstellt.

Das Laufzeitkonto bleibt reguläres Mitglied von `Domain Users`. Zusätzlich müssen auf dem Konto `SELF / Change Password` erlaubt und `User cannot change password` deaktiviert sein. Das Laufzeitkonto selbst benötigt weder administrative AD-Gruppen noch Reset-Password-Rechte; nur das getrennte Provisioning-Credential benötigt die Rechte zum Erstellen beziehungsweise Zurücksetzen.

Bei einer echten Installation kann der Installer das Servicekonto mit einem separaten AD-Provisioning-Credential vorbereiten. Fehlt das Konto, fragt er vor dem Erstellen und Aktivieren nach; ist es vorhanden, bietet er ein zufälliges komplexes Kennwort oder eine sichere manuelle Eingabe für den administrativen Reset an. Das Zielkennwort wird direkt als Current-Secret synchronisiert und nicht angezeigt. Der Installer bevorzugt LDAPS. Ist LDAPS nicht verfügbar und wurde der LDAP-Fallback ausdrücklich genehmigt, verwendet der Bootstrap das installierte Active-Directory-PowerShell-Modul über Active Directory Web Services gegen den konfigurierten Server. Nach Erstellung oder Reset bleibt **Benutzer muss Kennwort bei der nächsten Anmeldung ändern** deaktiviert. `-ValidateOnly` und `-WhatIf` verändern das AD-Konto nicht.

### PowerShell-Management-Modul

Das Modul `Deployment/HybridDeviceTagger.Management` stellt folgende Funktionen für die Entra-geschützte Management API bereit:

Eine vollständige Beschreibung der Voraussetzungen, Autorisierung, Datenmodelle, Befehle,
Nebenläufigkeitssteuerung, Fehlerbehandlung und internen Implementierung enthält die
[Modulreferenz](Deployment/HybridDeviceTagger.Management/README.md).

Nach der ersten Veröffentlichung kann das Modul aus der PowerShell Gallery installiert werden:

```powershell
Install-PSResource -Name HybridDeviceTagger.Management -Repository PSGallery
Import-Module HybridDeviceTagger.Management
```

| Funktion | Beschreibung |
| --- | --- |
| `Connect-HybridDeviceTagger` | Baut mit der vorhandenen Azure-CLI-Anmeldung die Verbindung zur Management API auf. Mit `-ApiBaseUri` und `-ClientId` kann eine andere Bereitstellung gewählt werden; `-PassThru` gibt nicht vertrauliche Verbindungsinformationen zurück. |
| `Get-HybridDeviceTaggerRule` | Gibt alle wirksamen Tagging-Regeln oder mit `-Name` eine bestimmte Regel zurück. Der Namensvergleich erfolgt ohne Beachtung der Groß- und Kleinschreibung. |
| `Get-HybridDeviceTaggerLastRunStatus` | Zeigt Status, Invocation-ID, Zeitstempel, Ergebniszähler und bereinigte Fehlerinformationen des letzten geplanten oder manuell gestarteten Tagging-Laufs. |
| `Get-HybridDeviceTaggerPasswordRotationStatus` | Zeigt den Status der letzten Kennwortprüfung beziehungsweise Rotation und den Zeitpunkt der letzten tatsächlichen Kennwortänderung. Kennwörter werden nicht zurückgegeben. |
| `Get-HybridDeviceTaggerTrace` | Lädt den letzten JSONL-Trace und gibt jedes Ereignis als strukturiertes `HybridDeviceTagger.TraceEvent`-Objekt für Filterung, Auswahl oder Export in der Pipeline aus. |
| `Get-HybridDeviceTaggerTrustedCertificate` | Listet die Metadaten aller aktuell für neue LDAPS-Verbindungen vertrauenswürdigen Zertifikate auf. Zertifikatsdaten und private Schlüssel werden nicht ausgegeben. |
| `Add-HybridDeviceTaggerRule` | Fügt eine vollständige Regel hinzu oder ersetzt eine gleichnamige Regel. Akzeptiert über `-Rule` eine einzelne Regel oder ein Objekt mit einer `Rules`-Eigenschaft; alle übrigen Regeln bleiben erhalten. |
| `Add-HybridDeviceTaggerTrustedCertificate` | Fügt mit `-Path` ein öffentliches DER- oder PEM-Zertifikat als LDAPS-Vertrauensanker hinzu. Vorhandene Zertifikate bleiben erhalten, Dateien mit privatem Schlüssel werden abgelehnt. |
| `Remove-HybridDeviceTaggerRule` | Entfernt die mit `-Name` angegebene Regel. Die API verhindert, dass dadurch keine gültige Tagging-Regel mehr verbleibt. |
| `Remove-HybridDeviceTaggerTrustedCertificate` | Entfernt das mit `-CertificateThumbprint` angegebene Zertifikat. Der letzte verbliebene LDAPS-Vertrauensanker kann nicht gelöscht werden. |
| `Initialize-HybridDeviceTaggerActiveDirectoryCredential` | Validiert das mit `-Password` als `SecureString` übergebene initiale AD-Servicekonto-Kennwort und lässt es durch die Managed Identity der Function im Key Vault speichern. Ein vorhandenes Current-Secret wird nicht überschrieben. |
| `Invoke-HybridDeviceTagging` | Startet synchron denselben Geräteabgleich wie der Timer und gibt Invocation-ID sowie Gesamtergebnis zurück. Ein paralleler Tagging-Lauf führt zu einem Konflikt. |
| `Invoke-HybridDeviceTaggerPasswordRotation` | Erzwingt synchron eine Kennwortrotation unabhängig vom Kennwortalter. Prüfung des aktuellen Kennworts, LDAPS, Staging, Validierung und Promotion bleiben verpflichtend. |

Zuerst muss `Connect-HybridDeviceTagger` ausgeführt werden; alle weiteren Funktionen verwenden diese Verbindung und erneuern das Zugriffstoken bei Bedarf. Schreibende Funktionen unterstützen `-WhatIf`. Das Initialisieren des AD-Kennworts, das Entfernen von Regeln oder Zertifikaten und die erzwungene Kennwortrotation erfordern standardmäßig eine Bestätigung mit hoher Auswirkung. Ausführliche Syntax, Parameter und Beispiele zeigt die integrierte Hilfe, beispielsweise mit `Get-Help Invoke-HybridDeviceTaggerPasswordRotation -Full`.

## Azure-Betrieb

Die Function App muss aus ihrem virtuellen Netzwerk den konfigurierten Domain Controller sowie die über die Forest-CrossRefs ermittelten Domain-DNS-Namen auflösen und per LDAPS erreichen, beispielsweise über Site-to-Site-VPN oder ExpressRoute. Optional kann LDAP auf Port 389 als Fallback erreichbar sein. Legen Sie alle Werte aus der Konfiguration als Function-App-Settings an und aktivieren Sie Managed Identity. Weisen Sie deren Service Principal die drei beschriebenen Graph-App-Rollen sowie die Key-Vault- und Blob-Rechte zu. Für Geräte in eingeschränkten administrativen Einheiten benötigt die Managed Identity zusätzlich die vom Installer erzeugte Least-Privilege-Rolle direkt am jeweiligen RMAU-Scope.

Bei CA-signierten LDAPS-Serverzertifikaten prüfen Installer und Function den Sperrstatus online und lehnen die Verbindung ab, wenn das Zertifikat gesperrt ist oder CRL beziehungsweise OCSP nicht innerhalb von zehn Sekunden erreichbar sind. Stellen Sie deshalb sicher, dass der Installationshost und das Function-Integrationssubnetz die im Zertifikat veröffentlichten Sperrinformations-Endpunkte erreichen. Für ein explizit als Vertrauensanker hinterlegtes selbstsigniertes Serverzertifikat entfällt die Sperrprüfung; Vertrauenskette, Servername, Gültigkeitszeitraum und Server-Authentication-EKU werden weiterhin geprüft.

Die Funktion markiert Geräte innerhalb der Ziel-OUs. Verlässt ein Computer alle konfigurierten Ziel-OUs, entfernt der nächste Lauf die noch unveränderten verwalteten Extension Attributes sowie die vom Tagger angelegten Gruppen-, AU- und RMAU-Mitgliedschaften.

## Problembehandlung

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

## Entwicklerdokumentation

Ausführliche Informationen zur Architektur, Management API, Konfiguration, lokalen Entwicklung, Tests und Versionierung stehen in [DEVELOPER.md](DEVELOPER.md).

## Lizenz

Dieses Projekt ist unter der [Apache License 2.0](LICENSE) lizenziert.
