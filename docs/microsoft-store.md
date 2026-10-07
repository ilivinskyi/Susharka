# Publishing to the Microsoft Store

Susharka ships to the Store as an MSIX package. The Store signs it, so no code-signing certificate is needed.

## 1. Set the package identity (once)

Copy [`installer/msix/store.example.json`](../installer/msix/store.example.json) to `installer/msix/store.json`.
That file is git-ignored, so your identity stays on your machine. Then, in Partner Center, open **Apps and
games → Susharka → Product management → Product identity** and copy the three values into it:

| store.json | Partner Center |
|---|---|
| `IdentityName` | Package/Identity/Name |
| `Publisher` | Package/Identity/Publisher (starts with `CN=`) |
| `PublisherDisplayName` | Package/Properties/PublisherDisplayName |

Without them the build still produces a package, named `...-LOCALTEST.msix`, for local testing only.

## 2. Build

```bash
pwsh ./build.ps1
```

This produces `dist\Susharka-<version>.0-x64.msix`. The Store requires the last part of the version to be 0,
which the build takes care of. Bump `<Version>` in `src/Susharka/Susharka.csproj` for every new submission.

To try the package on this PC before uploading (needs Developer Mode):

```bash
pwsh ./build.ps1 -SkipTests -SkipInstaller -RegisterMsix
```

Then start Susharka from the Start menu. Remove it with `Get-AppxPackage *Susharka* | Remove-AppxPackage`.

## 3. Submission

**Packages:** upload the `.msix`.

**Properties**
- Category: *Productivity* (or *Utilities & tools*)
- Privacy policy URL: `https://github.com/ilivinskyi/Susharka/blob/main/PRIVACY.md`
- Website: `https://github.com/ilivinskyi/Susharka`

**Restricted capability.** The package declares `runFullTrust`, as every classic desktop app does. When asked
why, answer:

> Susharka is a WPF desktop application packaged with MSIX. It needs full trust to register global keyboard
> shortcuts, capture the screen for screenshots, show a notification-area icon and draw a transparent
> overlay at the top of the screen.

**Age ratings:** complete the questionnaire. The app has no user-generated content sharing, chat,
purchases or network access.

**Store listing (English)**

*Description*

> Every screenshot, within reach.
>
> Susharka hangs your screenshots on a clothesline at the top of the screen. Take a screenshot and it flies
> up onto the line, pegged next to the last few you took. Rest the pointer at the top edge and the line
> drops down; move away and it pulls back up out of sight.
>
> • Press Ctrl + Shift + 4 and drag to capture any part of the screen
> • Click a photo to copy it, drag it into any app to share it
> • Press and hold to mark it up with pen, highlighter, arrows, shapes, text and crop
> • Drag a photo into a folder to move the file there
> • Snipping Tool and Win + PrtScn captures are hung on the line too
> • Shortcuts, reveal delay and number of photos are all adjustable
>
> No account, no network, no analytics. Your screenshots never leave your PC.

*Short description*

> Your screenshots hang on a clothesline at the top of the screen: click to copy, drag to share, hold to mark up.

*Keywords:* screenshot, snipping, screen capture, clipboard, annotate

*Screenshots* (1920 × 1080): [`docs/store`](store)

1. The line with your recent screenshots, at the top of the screen
2. Click a photo to copy it
3. Capture any part of the screen
4. Press and hold to mark up

*Store logo* (if asked for a 300 × 300 image): [`docs/store/logo-300.png`](store/logo-300.png)
