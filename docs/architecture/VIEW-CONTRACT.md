# View contract (engine → page)

`src/engine/View.fs` is the single source; `src/kernel/index.html` binds these keys. Every key is present in every
projection (Limen requires total projections).

- Global: `appStatus pageTitle route isHome isInvestigation isNotFound tabKey is{Overview,Hypotheses,Evidence,Questions,Causes,Timeline,Report} homeHref hasNotice noticeText noticeTone contractVersion engineVersion sourceCommit`
- Connection: `connState connLabel connDetail connTone isDisconnected isConnecting isConnected isRefused formOwner formRepo formBranch formRoot` (the token is never projected)
- Sync: `syncState syncLabel syncDetail syncTone syncBusy hasHeld canRetry canReapply canReconcile canDiscard canRefresh refreshDisabled`
- Dashboard: `investigations[] {id status statusText href title lifecycle lifecycleTone version updated hypotheses evidence contradictions unknowns hasContradictions hasUnknowns nextAction isValid isInvalid isLoading errorCode errorText} dashCount dashValid dashInvalid dashHasInvalid dashEmpty dashSummary canCreate createDisabled createReason targetLabel accessLabel headLabel sortBy`
- Investigation: `invTitle invId invLifecycle invLifecycleTone invVersion invCreated invUpdated invLocal invReadOnly invReadOnlyReason actionsBlocked actionsOpen invNext invEventCount invLoading invMissing invMissingText hasInvestigation tabs[] invActions[] observations[] hypotheses[] evidence[] relationships[] unknowns[] assumptions[] contradictions[] predictions[] actions[] findings[] negativeKnowledge[] causes[] corrections[] criteria[] verifications[] conclusions[] proposals[] timeline[] chronology[] timelineFilter timelineCount openUnknownCount openContradictionCount blockingCount hasBlockers currentConclusion hasConclusion hasCauses reportGenerated`
- Inspector: `hasSelection selKind selTitle selBody selMeta[] selActions[] selLinks[] selCloseHref selUrl hasSelUrl`
- Dialog: `dialogOpen dialogTitle dialogError hasDialogError dialogConfirm hasConfirm dialogConfirming dialogSubmitLabel dialogTarget {textA,textB,enumA,enumB,refA,refsB}{Label,Hidden,Help,Required,Value} enumAOptions[] enumBOptions[] refAOptions[] refsBOptions[]`

Semantic events (page → engine): `connect.{owner,repo,branch,root,token,submit,cancel} disconnect refresh cmd.new cmd.open(key=command) cmd.{textA,textB,enumA,enumB,refA} cmd.refsB(key,checked) cmd.submit cmd.cancel sync.{retry,reapply,reconcile,discard} auth.renew timeline.filter dashboard.sort notice.dismiss`.
