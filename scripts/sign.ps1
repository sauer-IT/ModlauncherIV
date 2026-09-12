<#
.SYNOPSIS
  Signiert Binaries mit Authenticode.

.DESCRIPTION
  Zwei Betriebsarten, gleicher Ablauf:

  * Release  — wenn die Umgebungsvariable MLIV_SIGN_THUMBPRINT gesetzt ist, wird
               dieses Zertifikat benutzt und die Signatur zusaetzlich
               zeitgestempelt. Das ist der Weg fuer ein echtes
               Codesigning-Zertifikat.

  * Entwicklung — sonst wird ein selbstsigniertes Zertifikat "CN=ModlauncherIV Dev"
               benutzt und beim ersten Mal angelegt.

  Zum Thema Smart App Control, damit hier keine falsche Erwartung entsteht:
  Eine SELBSTSIGNIERTE Signatur macht SAC NICHT zufrieden. SAC bewertet den Ruf
  des Signierers ueber Microsofts Intelligent Security Graph, nicht die lokale
  Vertrauenskette — ein selbstsigniertes Zertifikat hat dort keinen Ruf.
  Das Signieren hier dient dazu, dass die Release-Pipeline von Anfang an steht
  und spaeter nur das Zertifikat getauscht werden muss.

.EXAMPLE
  .\scripts\sign.ps1 -Path .\artifacts\fd\mliv.exe
  $env:MLIV_SIGN_THUMBPRINT = "ab12..."; .\scripts\sign.ps1 -Path .\artifacts\release\*.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Path,

    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Get-SigningCertificate {
    $thumbprint = $env:MLIV_SIGN_THUMBPRINT

    if ($thumbprint) {
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $thumbprint } |
                Select-Object -First 1

        if (-not $cert) { throw "Zertifikat mit Thumbprint $thumbprint nicht gefunden." }
        Write-Host "Signiere mit Release-Zertifikat: $($cert.Subject)"
        return [pscustomobject]@{ Certificate = $cert; IsRelease = $true }
    }

    $subject = "CN=ModlauncherIV Dev"
    $cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

    if (-not $cert) {
        Write-Host "Lege Entwicklungszertifikat an: $subject"
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
                -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3) `
                -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072
    }

    return [pscustomobject]@{ Certificate = $cert; IsRelease = $false }
}

$signer = Get-SigningCertificate
$files = $Path | ForEach-Object { Get-ChildItem $_ -File -ErrorAction SilentlyContinue } | Sort-Object FullName -Unique

if (-not $files) { throw "Keine Dateien zum Signieren gefunden: $($Path -join ', ')" }

foreach ($file in $files) {
    $params = @{
        FilePath      = $file.FullName
        Certificate   = $signer.Certificate
        HashAlgorithm = "SHA256"
    }

    # Zeitstempel nur beim Release: der Dev-Lauf soll ohne Internet funktionieren.
    if ($signer.IsRelease) { $params.TimestampServer = $TimestampUrl }

    $result = Set-AuthenticodeSignature @params
    "{0,-24} {1}" -f $file.Name, $result.Status
}
