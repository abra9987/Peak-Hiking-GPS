# Automation

The map resets at **17:00 UTC** daily. A capture should run shortly after.

## Why the game must run on a real desktop

PEAK renders through the GPU, and the capture reads pixels back from a render
texture. `-batchmode -nographics` disables rendering entirely, so there is
nothing to read. A headless server without a GPU cannot do this at all.

What works: a scheduled task on a machine with a display session, running under
the logged-in user. The window can be minimised; it cannot be absent.

## Scheduled capture (Windows)

Register a task that runs a few minutes after the reset:

```powershell
$repo = 'C:\path\to\Peak Map Interactive'

$action = New-ScheduledTaskAction `
    -Execute 'pwsh.exe' `
    -Argument "-NoProfile -File `"$repo\tools\daily.ps1`"" `
    -WorkingDirectory $repo

# 17:05 UTC, expressed in local time.
$utc = [datetime]::UtcNow.Date.AddHours(17).AddMinutes(5)
$trigger = New-ScheduledTaskTrigger -Daily -At $utc.ToLocalTime().TimeOfDay

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 45)

Register-ScheduledTask -TaskName 'PeakMapInteractive-Daily' `
    -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Capture and publish the PEAK daily map'
```

`-StartWhenAvailable` matters: if the machine was asleep at 17:05 the task runs
at the next opportunity rather than skipping the day.

`daily.ps1` is just the two steps in order:

```powershell
& "$PSScriptRoot\capture.ps1"
& "$PSScriptRoot\publish.ps1"
```

Because `capture.ps1` throws rather than publishing a bad snapshot, a failure
leaves yesterday's map up instead of replacing it with an empty one. That is the
right failure mode: a stale map is wrong in a way people can detect from the
timestamp, an empty one looks like the game changed.

## Keeping snapshots out of git history

**Never commit map data to a branch you keep history on.**

A snapshot is roughly 2 MB of heightfield plus a few MB of orthophoto per
segment. Committing that daily means every clone forever carries every map that
has ever existed, and git cannot compress across them because the bytes are
unrelated noise from one day to the next.

This is not hypothetical. The reference project this one improves on commits its
JPEGs into `main` daily. At 87 commits its repository is **2.05 GB** — around
99.9% of it maps nobody will ever look at again. GitHub starts warning past 1 GB
and refuses to serve some operations past 5 GB, so this ends the project's
hosting within about a year.

Snapshots have no useful diff and no archival value beyond the current day.
`tools/publish.ps1` therefore builds the site into a scratch repository, commits
once, and force-pushes:

```
main       source history, no binaries, grows slowly and usefully
gh-pages   always exactly one commit: today's site + today's data
```

Rewriting a published branch is normally something to be careful about. Here it
is the point — nothing on `gh-pages` is authored, all of it is regenerated from
`main` plus a capture, and there is nothing to lose.

If you later want an archive of past maps, put it somewhere built for blobs:
GitHub Releases, or object storage. Not git history.

## Verifying a capture

`capture.ps1` refuses to publish when:

- the manifest's schema version is not the one the toolchain understands
- no segments were captured
- any segment measured under 25% terrain coverage

Low coverage almost always means the capture ran before the biome finished
streaming in, which produces a map that looks plausible and is missing half its
geometry. Failing loudly is cheaper than finding out from a bug report.

## First run

Set `WriteDiagnostics = true` in the plugin config before the first capture. The
run then writes `diagnostics.json` listing every component type the marker
registry did not recognise, sorted by frequency. That list is how you find out
what PEAK actually calls the things you want on the map — far faster than
decompiling, and it stays accurate across game updates.
