namespace Causa.Contract

open System
open IndyInitRootCauseInvestigator.Engine

/// The derived state projection (CONTRACT §8). Never authoritative: always recomputable
/// from the event history through the engine's Evolve.replay.
[<RequireQualifiedAccess>]
module Projection =
    open Fields

    let private provenance (p: Provenance) : Result<JValue, CausaError> =
        result {
            let! actor = EventWire.encodeActor p.Actor
            let! at = ts "provenance.recordedAt" p.RecordedAt
            return JObj [ "actor", actor; "recordedAt", at ]
        }

    /// Entity arrays are ordered by `id` using ordinal comparison of the UUID text (CONTRACT §8.3).
    let private entities (render: 'V -> Result<string * (string * JValue) list, CausaError>) (provenanceOf: 'V -> Provenance) (m: Map<'K, 'V>) =
        m
        |> Map.toList
        |> List.map snd
        |> traverse (fun v ->
            result {
                let! (key, fields) = render v
                let! p = provenance (provenanceOf v)
                return key, JObj(fields @ [ "provenance", p ])
            })
        |> Result.map (List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b)) >> List.map snd >> JArr)

    let private key (i: Id<'Tag>) = Uuid.text i.Value

    let private change (c: HypothesisChange) : Result<JValue, CausaError> =
        result {
            let! command =
                match Enums.hypothesisCommand.All |> List.tryFind (fun x -> Enums.hypothesisCommandEngineName x = c.Command) with
                | Some x -> Ok(enum Enums.hypothesisCommand x)
                | None -> unrepresentable ("hypothesis command '" + c.Command + "' is not registered")
            let! at = ts "history.at" c.At
            let! evidence = ids "history.evidenceIds" c.Evidence
            return
                JObj
                    [ "from", enum Enums.hypothesisState c.From; "to", enum Enums.hypothesisState c.To; "command", command
                      "reason", text c.Reason; "evidenceIds", evidence; "at", at; "aggregateVersion", JInt c.Sequence ]
        }

    let state (inv: Investigation) : Result<JValue, CausaError> =
        result {
            let! createdAt = ts "createdAt" inv.CreatedAt
            let! observations =
                inv.Observations
                |> entities (fun o ->
                    optTs "observedAt" o.ObservedAt
                    |> Result.map (fun t -> key o.Id, [ "id", id o.Id; "original", text o.Original; "observedAt", t ])) (fun o -> o.Provenance)
            let! hypotheses =
                inv.Hypotheses
                |> entities (fun h ->
                    h.History |> traverse change
                    |> Result.map (fun history ->
                        key h.Id,
                        [ "id", id h.Id; "statement", text h.Statement; "mechanism", optText h.Mechanism
                          "state", enum Enums.hypothesisState h.State; "history", JArr history ])) (fun h -> h.Provenance)
            let! evidence =
                inv.Evidence
                |> entities (fun e ->
                    result {
                        let! captured = optTs "capturedAt" e.CapturedAt
                        let kind, content =
                            match e.Content with
                            | TextEvidence t -> "text", t.Value
                            | ArtifactEvidence u -> "artifact", u
                        return
                            key e.Id,
                            [ "id", id e.Id; "contentKind", JStr kind; "content", JStr content; "category", enum Enums.evidenceCategory e.Category
                              "source", text e.Source; "capturedAt", captured; "verification", enum Enums.verificationStatus e.Verification
                              "supersededBy", (e.SupersededBy |> Option.map id |> Option.defaultValue JNull)
                              "admittedAtVersion", JInt e.AdmittedAt ]
                    }) (fun e -> e.Provenance)
            let! relationships =
                inv.Relationships
                |> entities (fun r ->
                    Ok(key r.Id,
                       [ "id", id r.Id; "evidenceId", id r.Evidence; "hypothesisId", id r.Hypothesis; "kind", enum Enums.relationshipKind r.Kind
                         "rationale", optText r.Rationale ])) (fun r -> r.Provenance)
            let! unknowns =
                inv.Unknowns
                |> entities (fun u ->
                    Ok(key u.Id,
                       [ "id", id u.Id; "question", text u.Question; "materiality", enum Enums.materiality u.Materiality
                         "state", enum Enums.unknownState u.State; "basis", optText u.Basis ])) (fun u -> u.Provenance)
            let! assumptions =
                inv.Assumptions
                |> entities (fun a ->
                    Ok(key a.Id,
                       [ "id", id a.Id; "proposition", text a.Proposition; "materiality", enum Enums.materiality a.Materiality
                         "state", enum Enums.assumptionState a.State ])) (fun a -> a.Provenance)
            let! contradictions =
                inv.Contradictions
                |> entities (fun c ->
                    ids "evidenceIds" c.Evidence
                    |> Result.map (fun ev ->
                        key c.Id,
                        [ "id", id c.Id; "statement", text c.Statement; "evidenceIds", ev; "materiality", enum Enums.materiality c.Materiality
                          "state", enum Enums.contradictionState c.State; "basis", optText c.Basis ])) (fun c -> c.Provenance)
            let! predictions =
                inv.Predictions
                |> entities (fun p ->
                    Ok(key p.Id,
                       [ "id", id p.Id; "hypothesisId", id p.Hypothesis; "statement", text p.Statement
                         "strength", enum Enums.predictionStrength p.Strength; "timing", enum Enums.predictionTiming p.Timing ])) (fun p -> p.Provenance)
            let! actions =
                inv.Actions
                |> entities (fun a ->
                    ids "targetHypothesisIds" a.Targets
                    |> Result.map (fun t ->
                        key a.Id,
                        [ "id", id a.Id; "kind", enum Enums.actionKind a.Kind; "purpose", text a.Purpose; "targetHypothesisIds", t
                          "state", enum Enums.actionState a.State ])) (fun a -> a.Provenance)
            let! findings =
                inv.Findings
                |> entities (fun f ->
                    Ok(key f.Id,
                       [ "id", id f.Id; "actionId", id f.Action; "originalResult", text f.OriginalResult; "interpretation", optText f.Interpretation ]))
                    (fun f -> f.Provenance)
            let! negative =
                inv.NegativeKnowledge
                |> entities (fun n ->
                    ids "basisEvidenceIds" n.Basis
                    |> Result.map (fun b -> key n.Id, [ "id", id n.Id; "statement", text n.Statement; "scope", text n.Scope; "basisEvidenceIds", b ]))
                    (fun n -> n.Provenance)
            let! causes =
                inv.Causes
                |> entities (fun c ->
                    let members = c.Members |> List.map (fun m -> JObj [ "hypothesisId", id m.Hypothesis; "role", enum Enums.causeRole m.Role ])
                    Ok(key c.Id,
                       [ "id", id c.Id; "outcome", text c.Outcome; "members", JArr members; "mechanism", optText c.Mechanism
                         "state", enum Enums.causeState c.State ])) (fun c -> c.Provenance)
            let! corrective =
                inv.CorrectiveActions
                |> entities (fun ca ->
                    ids "targetCauseIds" ca.Targets
                    |> Result.map (fun t ->
                        key ca.Id,
                        [ "id", id ca.Id; "kind", enum Enums.correctiveActionKind ca.Kind; "description", text ca.Description; "targetCauseIds", t
                          "state", enum Enums.correctiveActionState ca.State; "awaitingSinceVersion", JInt ca.AwaitingSince ])) (fun ca -> ca.Provenance)
            let! criteria =
                inv.Criteria
                |> entities (fun c ->
                    Ok(key c.Id,
                       [ "id", id c.Id; "correctiveActionId", id c.CorrectiveAction; "expectedOutcome", text c.ExpectedOutcome; "method", optText c.Method ]))
                    (fun c -> c.Provenance)
            let! verifications =
                inv.Verifications
                |> entities (fun v ->
                    ids "evidenceIds" v.Evidence
                    |> Result.map (fun ev ->
                        key v.Id,
                        [ "id", id v.Id; "criterionId", id v.Criterion; "outcome", enum Enums.verificationOutcome v.Outcome; "evidenceIds", ev
                          "recordedAtVersion", JInt v.Sequence ])) (fun v -> v.Provenance)
            let! conclusions =
                inv.Conclusions
                |> entities (fun c ->
                    ids "causeIds" c.Causes
                    |> Result.map (fun cs ->
                        key c.Id,
                        [ "id", id c.Id; "statement", text c.Statement; "strength", enum Enums.conclusionStrength c.Strength; "causeIds", cs
                          "residualUncertainty", optText c.ResidualUncertainty; "recordedAtVersion", JInt c.Sequence ])) (fun c -> c.Provenance)
            let! proposals =
                inv.Proposals
                |> entities (fun p ->
                    Ok(key p.Id,
                       [ "id", id p.Id; "kind", enum Enums.proposalKind p.Kind; "text", text p.Text; "rationale", optText p.Rationale
                         "state", enum Enums.proposalState p.State; "decision", optText p.Decision ])) (fun p -> p.Provenance)
            return
                JObj
                    [ "investigationId", id inv.Id
                      "title", text inv.Title
                      "lifecycle", enum Enums.lifecycle inv.Lifecycle
                      "version", JInt inv.Version
                      "createdAt", createdAt
                      "observations", observations
                      "hypotheses", hypotheses
                      "evidence", evidence
                      "relationships", relationships
                      "unknowns", unknowns
                      "assumptions", assumptions
                      "contradictions", contradictions
                      "predictions", predictions
                      "actions", actions
                      "findings", findings
                      "negativeKnowledge", negative
                      "causes", causes
                      "correctiveActions", corrective
                      "criteria", criteria
                      "verifications", verifications
                      "conclusions", conclusions
                      "proposals", proposals ]
        }

    /// Snapshot envelope; generatedAt is the occurredAt of the last applied event (CONTRACT §8.1),
    /// which keeps snapshots byte-reproducible.
    let snapshot (inv: Investigation) (lastEventAt: DateTimeOffset) : Result<JValue, CausaError> =
        result {
            let! s = state inv
            let! generated = ts "generatedAt" lastEventAt
            return
                JObj
                    [ "formatVersion", JStr Contract.formatVersion
                      "investigationId", id inv.Id
                      "aggregateVersion", JInt inv.Version
                      "projectionVersion", JInt Contract.projectionVersion
                      "semanticVersion", JStr Contract.semanticVersion
                      "generatedAt", generated
                      "state", s ]
        }
