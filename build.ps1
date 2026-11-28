$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $references = 'WindowsBase', 'PresentationCore', 'PresentationFramework' | ForEach-Object {
        '/reference:' + (Join-Path $framework "WPF\$_.dll")
    }
    $sources = Get-ChildItem -LiteralPath src -Filter '*.cs' | ForEach-Object FullName
    $retiredAudio = 'meteor.wav', 'meteorhook.wav', 'meteorslingshot.wav', 'meteortwin.wav', 'meteorswitchback.wav'
    $audio = Get-ChildItem -LiteralPath assets -Filter '*.wav' | Where-Object { $_.Name -notin $retiredAudio } | ForEach-Object { '/resource:' + $_.FullName + ',' + $_.Name }
    & (Join-Path $framework 'csc.exe') /nologo /target:winexe /optimize+ /codepage:65001 /out:BlueStar.exe /win32icon:assets/blue-star.ico /resource:assets/blue-star.png,blue-star.png /resource:assets/leg-atlas.png,leg-atlas.png /resource:assets/galaxy-disc.png,galaxy-disc.png /resource:assets/meal-lens.ps,meal-lens.ps /resource:src/Panel.xaml,Panel.xaml /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Configuration.dll $references $audio $sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}
