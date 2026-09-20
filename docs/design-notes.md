# Compact two-provider layout

The two existing 276 x 64 windows keep the same ring, headline, secondary line,
and refresh control. Provider text distinguishes them without relying on color.
Codex uses blue; Claude uses terracotta. Each window has its own theme, position,
and visibility. Hover provides exact local-time details without widening the bar.

The visual contract: within five seconds, identify the provider and remaining
allowance; within twenty seconds, find reset details or hide the unused provider.

The reference review examined these patterns, adapting hierarchy rather than
copying a product's design:

- [Elicit usage](https://mobbin.com/screens/3a0f60e0-0ed9-46b0-bd0e-5b1b5e13e690): put a reset time near the usage figure and make an exhausted limit explicit.
- [StackAI usage](https://mobbin.com/screens/a2976bbf-d4f1-407f-8c56-88779215f037): align repeated categories consistently and retain numerical labels.
- [Gemini usage limits](https://mobbin.com/screens/1cb3c785-d4df-4a3a-992d-e887d1129667): keep current-window and weekly limits distinct with progressive detail.

Provider-specific limitations matter more than decorative motion. Claude shows
the age of the last status-line sample. Only a near-expiry Codex banked reset
enables the existing ember; hidden windows and disabled Windows UI effects stop it.

## Public media

The same repeated-row and progressive-detail patterns above inform the examples.
The overview compares both providers and themes. A separate taskbar view explains
where the percentage icons live. The animation shows the amber and red expiry
states without competing motion. Static and native-size alternatives are linked.

Bar images are drawn from the real paint methods at 4x resolution. Composite
images render at 2x, with no upscaling of small captures. Taskbar surroundings
are explicitly a mock. All values are synthetic. The GIF uses the same phase
increments as the app, with a labelled jump between demo expiry states.
