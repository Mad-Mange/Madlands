Describe 'Local realm startup' {
    . (Join-Path $PSScriptRoot 'start-local.ps1') -NoClient

    BeforeEach {
        Mock Write-Host {}
        Mock Invoke-Compose { 0 }
        Mock Start-Process { throw 'An unexpected process was started' }
    }

    It 'reuses a running account API without starting a server or Docker' {
        Mock Test-AccountApi { $true }
        Start-LocalRealm -NoClient
        Assert-MockCalled Invoke-Compose -Times 0 -Exactly -Scope It
        Assert-MockCalled Start-Process -Times 0 -Exactly -Scope It
    }

    It 'refuses to start when another service occupies the account port' {
        Mock Test-AccountApi { $false }
        Mock Test-LocalPort { $true }
        { Start-LocalRealm -NoClient } | Should Throw 'Port 7780 is already in use, but the Madlands account API did not respond. No server was started.'
        Assert-MockCalled Invoke-Compose -Times 0 -Exactly -Scope It
        Assert-MockCalled Start-Process -Times 0 -Exactly -Scope It
    }

    It 'starts the database and waits for the account API when the port is free' {
        $script:apiChecks = 0
        Mock Test-AccountApi { $script:apiChecks++; $script:apiChecks -ge 2 }
        Mock Test-LocalPort { param($Port) $Port -eq 15432 }
        Mock Start-Process {
            $process = [pscustomobject]@{ HasExited = $false }
            $process | Add-Member -MemberType ScriptMethod -Name Refresh -Value {}
            $process
        }
        Start-LocalRealm -NoClient
        Assert-MockCalled Invoke-Compose -Times 2 -Exactly -Scope It
        Assert-MockCalled Start-Process -Times 1 -Exactly -Scope It
    }

    It 'does not launch a server when PostgreSQL cannot be started' {
        Mock Test-AccountApi { $false }
        Mock Test-LocalPort { $false }
        Mock Invoke-Compose { 1 }
        { Start-LocalRealm -NoClient } | Should Throw 'Could not start PostgreSQL. Check that Docker Desktop is running.'
        Assert-MockCalled Start-Process -Times 0 -Exactly -Scope It
    }

    It 'reports when the server process exits before the API starts' {
        Mock Test-AccountApi { $false }
        Mock Test-LocalPort { param($Port) $Port -eq 15432 }
        Mock Start-Process {
            $process = [pscustomobject]@{ HasExited = $true }
            $process | Add-Member -MemberType ScriptMethod -Name Refresh -Value {}
            $process
        }
        { Start-LocalRealm -NoClient } | Should Throw
        Assert-MockCalled Start-Process -Times 1 -Exactly -Scope It
    }
}
