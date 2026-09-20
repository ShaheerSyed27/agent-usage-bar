# Initial publication checks

Checked on Windows on 21 September 2026 for Agent Usage Bar 2.0.0.

- Both executables compile with the Windows .NET Framework compiler, without package downloads.
- `scripts/test-local.ps1`: 21 focused checks pass. These cover the compiled
  bridge and PowerShell wrapper, private-field filtering, percentage conversion,
  absent and expired limits, malformed values, future timestamps, executable
  discovery, configuration backups, idempotent setup, safe removal, custom
  status-line preservation, provider-specific primary windows, and exclusion of
  the documentation renderer from production builds.
- Compared ten native tray-icon cases against the previous build, covering both
  providers and full, normal, low, critical, and unknown states. Every pixel was
  identical after extracting the shared drawing function.
- Gitleaks 8.30.1 reports no leaks in the project. An additional local scan found
  no personal paths, private email, or credential-pattern matches in publication files.
- Inspected the real renderer at 276 x 64 for Codex and Claude in light and dark
  mode. Documentation now renders vectors and GDI text at export resolution,
  rather than enlarging a tiny capture. The overview and taskbar example are
  2560 x 1280; local-time expiry details are 2560 x 1440.
- The 1680 x 640 GIF contains 80 frames with a 100 ms delay, an eight-second
  duration, and an infinite loop. The encoder verifies dimensions, duration,
  frame count, and changing pixels in both the amber and red motion states.
  A shared adaptive palette removes the previous noisy GIF dithering.
- The two-provider taskbar illustration is explicitly labelled as a mock, using
  the same tray icon drawing code as the app. A separate, unmodified taskbar
  crop was supplied for publication after the capture tool did not expose it.
  Both are visible in the README, with their provenance clearly distinguished.
  The crop matches the supplied file's SHA-256 and contains no text or EXIF chunks.
- Opened the published branch as a signed-out GitHub visitor. All four README
  images loaded at their intended source dimensions, none inside a collapsed
  details section. The GIF plays from GitHub's playback button; the README
  includes that hint for browsers which initially pause animations.
- The application starts locally as one process. Its Claude connection waits
  for a normal status-line report; no model request was generated for testing.
- The public file list contains source, scripts, documentation, and demo images.
  It excludes binaries, local settings, usage caches, logs, and credentials.

Claude's documented schema is tested with fixtures. A fresh live Claude account
response, WSL setup, other subscription plans, screen-reader behavior, and
every Windows display-scaling combination have not been independently verified.
The images are examples, not evidence of a live Claude account connection.

These checks are not an independent security audit or a promise of zero bugs.
