# Reporting issues

Issues go on the [Issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues) of the app's sp-mod page - bugs, wording, translations, feature requests or questions. The tab is in English.

## Report a problem
The app builds the report for you: the error, your app, SPT and Windows versions, a few settings and the app's log, with personal details taken out. You see the exact text first. Nothing is sent until you press **Open Issues tab**.

It opens from:
- **Help - Report a problem**, for anything. It holds your setup and the log.
- **Details** on a failed download card, under the red bar on **App update**, and beside the status line on **Mod lists** after an apply stops.
- An unexpected error. The window opens by itself.
- The next start after the app closed unexpectedly. It is offered once.

## Sending it
1. Press **Open Issues tab**. The app uploads the log, copies the report and opens the Issues tab.
2. Choose **Bug** and set **Affected version** to the version the window names.
3. Paste the title. The window has its own copy button for it.
4. Click in **Description**, press Ctrl+A, then Ctrl+V. The report replaces the form's template, log link included - don't upload the log by hand.
5. Fill in the steps and what you expected.

**Copy** copies the same report with the log inside it and uploads nothing - for the Discord or anywhere else.

## What is taken out
- your Windows user folder and user name
- the computer's name
- SPT profile and item ids - each becomes a short code, the same each time
- IP addresses with a port, apart from this machine's own
- your Server Map host, shared key and pinned certificate

The SPT install folder stays, minus your user folder - which drive and layout you use often matters. Tick **Include my installed mods** to add each mod's name, version and sp-mod id. It is off by default.

## Where the log goes
**Open Issues tab** uploads the log part to [codepaste.sp-mod.com](https://codepaste.sp-mod.com/). It is encrypted in the app before it leaves, and the key is only in the link. Anyone with the link can read it. It is deleted after 3 months. If the upload fails, the log goes into the copied report instead and the window says why.

## A more detailed log
For a problem that is hard to pin down, put an empty file named `verbose` (no extension) in `Data\logs` or next to `TCFModManager.exe`, restart the app and make it happen again before reporting.
