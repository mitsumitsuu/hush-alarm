# HUSH Alarm — decisions

## Adopted
- Windows native desktop app and iOS native app. Advertising, purchases, analytics, server connections, and cloud sync are absent.
- Explicitly selected headphone endpoint on Windows; WASAPI opens its concrete endpoint ID and never opens the default device.
- On iOS, approve the connected headphone route once, validate its UID before starting, and stop on any route change or interruption. No audible notifications or AlarmKit fallback.
- Each device owns its alarms. Only one active alarm per device in v1. Time-of-day and duration modes, three synthesized sounds, gradual volume, stop, and short snooze.
- Local authentication: encrypted PIN verifier on Windows; system device-owner authentication on iPhone. No account, password, or alarm data leaves the device.
- Free iPhone installation with AltStore Classic on Windows; user accepted renewing before the 7-day expiry. User enters Apple credentials directly in the installer.
- Public source-only repository for standard free macOS CI; unsigned IPA is signed locally by the user's installer. No signing credentials in CI. Only small release artifacts, not paid runners or artifact caches.
- Physical disconnect and locked-iPhone tests remain required before relying on the app. Do not describe untested audio behavior as verified.

## Rejected
- Browser-only/PWA: cannot establish the required iPhone background and routing behavior safely.
- System Clock / audible notification / AlarmKit backup: may sound on the phone speaker.
- Automatically resuming after reconnect: could choose a new route without user action.
- Paid Apple membership, paid build services, advertising, and subscriptions: conflict with the zero-cost requirement.
- Audio streaming and cloud synchronization: unnecessary data traffic for a local alarm.

## Acceptance
- No playback starts without a selected, approved, active headphone route.
- Disconnect cancels pending/ringing audio; reconnection never resumes automatically.
- Offline time-of-day and duration scheduling, stop, snooze, and authentication work.
- Windows compile and logic tests; iOS compile and core tests on macOS CI; physical checks separately recorded.
