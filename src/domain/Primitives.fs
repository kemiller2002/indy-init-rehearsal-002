namespace IndyInitRootCauseInvestigator.Engine

open System

/// Typed identifier: a Guid tagged with the entity kind it names (DEC-06).
/// Ids are created by the caller and travel inside commands so `decide`
/// stays a pure, deterministic function.
[<Struct; CustomEquality; CustomComparison>]
type Id<'Tag> =
    | Id of Guid

    member this.Value = let (Id g) = this in g
    override this.ToString() = this.Value.ToString("D")
    override this.Equals other =
        match other with
        | :? Id<'Tag> as o -> o.Value = this.Value
        | _ -> false
    override this.GetHashCode() = this.Value.GetHashCode()
    interface IComparable with
        member this.CompareTo other =
            match other with
            | :? Id<'Tag> as o -> compare this.Value o.Value
            | _ -> invalidArg "other" "different id kind"

[<RequireQualifiedAccess>]
module Id =
    let create<'Tag> (g: Guid) : Id<'Tag> = Id g
    let tryParse<'Tag> (text: string) : Id<'Tag> option =
        match Guid.TryParse text with
        | true, g when g <> Guid.Empty -> Some(Id g)
        | _ -> None
    let text (id: Id<'Tag>) = id.ToString()

// Phantom tags, one per core entity kind.
type InvestigationTag = interface end
type ObservationTag = interface end
type HypothesisTag = interface end
type EvidenceTag = interface end
type RelationshipTag = interface end
type UnknownTag = interface end
type AssumptionTag = interface end
type ContradictionTag = interface end
type PredictionTag = interface end
type ActionTag = interface end
type FindingTag = interface end
type NegativeKnowledgeTag = interface end
type CausalAssertionTag = interface end
type CorrectiveActionTag = interface end
type CriterionTag = interface end
type VerificationTag = interface end
type ConclusionTag = interface end
type ProposalTag = interface end

type InvestigationId = Id<InvestigationTag>
type ObservationId = Id<ObservationTag>
type HypothesisId = Id<HypothesisTag>
type EvidenceId = Id<EvidenceTag>
type RelationshipId = Id<RelationshipTag>
type UnknownId = Id<UnknownTag>
type AssumptionId = Id<AssumptionTag>
type ContradictionId = Id<ContradictionTag>
type PredictionId = Id<PredictionTag>
type ActionId = Id<ActionTag>
type FindingId = Id<FindingTag>
type NegativeKnowledgeId = Id<NegativeKnowledgeTag>
type CausalAssertionId = Id<CausalAssertionTag>
type CorrectiveActionId = Id<CorrectiveActionTag>
type CriterionId = Id<CriterionTag>
type VerificationId = Id<VerificationTag>
type ConclusionId = Id<ConclusionTag>
type ProposalId = Id<ProposalTag>

/// Text that is never empty or whitespace. The original string is kept
/// byte-for-byte (D-002: normalization never overwrites the source).
type NonEmptyText =
    private
    | NonEmptyText of string

    member this.Value = let (NonEmptyText s) = this in s
    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module NonEmptyText =
    let create (text: string) : Result<NonEmptyText, string> =
        if String.IsNullOrWhiteSpace text then Error "text must not be empty"
        else Ok(NonEmptyText text)

    let tryCreate (text: string) = create text |> Result.toOption
    let value (t: NonEmptyText) = t.Value

type ActorKind =
    | Human
    | Agent of provider: string * model: string
    | System

type Actor = { Id: string; Kind: ActorKind }

[<RequireQualifiedAccess>]
module Actor =
    let human id = { Id = id; Kind = Human }
    let agent id provider model = { Id = id; Kind = Agent(provider, model) }
    let system = { Id = "system"; Kind = System }

type Provenance = { Actor: Actor; RecordedAt: DateTimeOffset }

/// A reference to any entity inside an investigation, for heterogeneous links.
type EntityKind =
    | ObservationEntity
    | HypothesisEntity
    | EvidenceEntity
    | UnknownEntity
    | AssumptionEntity
    | ContradictionEntity
    | PredictionEntity
    | ActionEntity
    | FindingEntity
    | NegativeKnowledgeEntity
    | CausalAssertionEntity
    | CorrectiveActionEntity
    | CriterionEntity
    | VerificationEntity
    | ConclusionEntity
    | ProposalEntity

type EntityRef = { Kind: EntityKind; Id: Guid }
