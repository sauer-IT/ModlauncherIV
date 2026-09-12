<#
.SYNOPSIS
  Signs binaries with Authenticode.

.DESCRIPTION
  Two modes, same procedure:

  * Release     - when the environment variable MLIV_SIGN_THUMBPRINT is set,
                that certificate is used and the signature is time-stamped as
                well. That is the route for a real code-signing certificate.


  * Development - otherwise a self-signed certificate "CN=ModlauncherIV Dev"
                is used, and created the first time round.

  About Smart App Control, so no false expectation forms here: a SELF-SIGNED
  signature does NOT satisfy SAC. SAC judges the signer's reputation through
  Microsoft's Intelligent Security Graph, not the local trust chain - and a
  self-signed certificate has no reputation there. Signing here exists so the
  release pipeline stands from the start and only the certificate has to be
  swapped later.

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

        if (-not $cert) { throw "No certificate with thumbprint $thumbprint found." }
        Write-Host "Signing with the release certificate: $($cert.Subject)"
        return [pscustomobject]@{ Certificate = $cert; IsRelease = $true }
    }

    $subject = "CN=ModlauncherIV Dev"
    $cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1

    if (-not $cert) {
        Write-Host "Creating a development certificate: $subject"
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
                -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3) `
                -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072
    }

    return [pscustomobject]@{ Certificate = $cert; IsRelease = $false }
}

$signer = Get-SigningCertificate
$files = $Path | ForEach-Object { Get-ChildItem $_ -File -ErrorAction SilentlyContinue } | Sort-Object FullName -Unique

if (-not $files) { throw "No files to sign found: $($Path -join ', ')" }

foreach ($file in $files) {
    $params = @{
        FilePath      = $file.FullName
        Certificate   = $signer.Certificate
        HashAlgorithm = "SHA256"
    }

    # Time stamp only for releases: the dev run should work without internet.
    if ($signer.IsRelease) { $params.TimestampServer = $TimestampUrl }

    $result = Set-AuthenticodeSignature @params

    # With a self-signed certificate "UnknownError" does not mean signing failed
    # - the signature is on the file. It means the chain ends at a root this
    # machine does not trust, and with a dev certificate that is exactly what to
    # expect.
    #
    # Reporting that unfiltered as an error has already cost a quarter of an
    # hour of debugging once. So distinguish here.
    $note = switch ($result.Status) {
        "Valid"        { "signed and trusted" }
        "UnknownError" { if ($signer.IsRelease) { "ERROR: $($result.StatusMessage)" }
                         else { "signed (dev certificate, nobody trusts the root - expected)" } }
        default        { "ERROR: $($result.Status) - $($result.StatusMessage)" }
    }

    "{0,-24} {1}" -f $file.Name, $note

    if ($note -like "ERROR*") { $script:failed = $true }
}

if ($script:failed) { throw "At least one file could not be signed." }
