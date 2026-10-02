param(
    [Parameter(Mandatory=$true)][string]$SourceLayout,
    [Parameter(Mandatory=$true)][string]$Capture,
    [Parameter(Mandatory=$true)][string]$OutputLayout,
    [Parameter(Mandatory=$true)][string]$SourceRepair,
    [Parameter(Mandatory=$true)][string]$OutputRepair
)
throw 'Disabled: topology keys are lossy and originalPoints includes user-edited coordinates. Rebasing this way destroys published edits.'
