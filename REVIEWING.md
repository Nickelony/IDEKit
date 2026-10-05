# Reviewing

How a review of this repository runs and reports: each category gets a verdict, backed by the rules it cites, and
no score.

## Verdicts

| Verdict | When |
| --- | --- |
| **Ship** | No rule is broken, and no defect reaches a host or a user. |
| **Ship and file** | Each break is one that `BACKLOG.md` already records, and each new finding is a suggestion the review files there. |
| **Needs work** | A finding cites a broken rule, or a defect that ordinary use reaches: wrong behavior, a fix that breaks code or does not settle, a page that states something false, or a test that cannot fail. |

A rule is a rule of [CONVENTIONS.md](CONVENTIONS.md) cited by its id, a documentation rule of [AGENTS.md](AGENTS.md),
or a rule of the [architecture guide](docs/ArchitectureGuide.md) cited by its section. A Critical or High finding is
always **Needs work**, and an Edge finding never is. A finding that cites no rule and names no defect is a suggestion,
however strongly the reviewer holds it.

## Severity

A finding's severity weighs what it breaks against whether ordinary use reaches it.

| Severity | When |
| --- | --- |
| **Critical** | Ordinary use hits it, and it loses or corrupts a document or a file, changes what a host's code or a user's files mean, breaks a host's build, or crashes or hangs the host. |
| **High** | Ordinary use hits it, and a host has to work around it, as with a wrong result, a fix that does not settle, or a false report a user would turn off. |
| **Medium** | Ordinary use hits it, and it costs a missed result, a cosmetic flaw or a page that misleads. |
| **Low** | Only a maintainer pays for it, as with a name, a duplicate or a missing test. |
| **Edge** | Only contrived input reaches it: input no one would write, or a state the code cannot produce. |

- A finding is reachable when the reviewer shows a host call, a document, a path or a server message that a host or a
  user would produce. A finding without one is an edge.
- A mistake a host or a user plausibly makes, such as a typo, a wrong path or an unsupported value, is ordinary use,
  and a clear refusal of it is correct.
- A silent wrong result, a crash or a hang on such a mistake is graded as the table says, and a deliberate attempt to
  break the library is a Security question.
- An edge goes to `BACKLOG.md` as a suggestion. One that loses data or changes what a host's code or a user's files mean
  is **Medium** however rare it is, because nobody sees that harm in a diff they did not expect.
- A structural finding, such as a layer, a contract or a duplicate that the next change has to work around, is judged
  by the debt it builds, not by how often a host meets it, and is at least **Medium**.

## Categories

| Category | Judged by |
| --- | --- |
| Correctness | the defects above, reproduced, including races, cancellation and teardown |
| Architecture | the S rules and the rules of the architecture guide |
| Names | the I, N, W, M and P rules |
| Contracts | the C, K and L rules |
| Tests | the T rules, and whether each test can fail |
| Docs | the documentation rules of AGENTS.md, and whether each page states what the code does |
| Host integration | each scenario of the guides, built from the docs and the samples alone: a step that fails as written is a defect |
| Build and packaging | the commands of the "Build and verify" section of AGENTS.md, the public API baseline, and the metadata and readme of each package |
| Performance | a measured cost on a document or a workspace a user edits |
| Security | what a hostile file, path or language-server message can make the library do |

## How a review runs

A region whose files have not changed since a review gave it **Ship** or **Ship and file** keeps that verdict and is
not reviewed again.

1. Run the build and test commands of AGENTS.md. A failing command is **Needs work** for its category. A review
   with several reviewers runs them once and shares the output, because two builds of one tree collide. A failure
   that the change did not cause, that `BACKLOG.md` records, or that `tests/TestSupport/README.md` lists as
   environment-dependent is reported apart and does not decide the verdict, and an inconclusive test is no failure.
2. Trust the guards for each guarded rule, and read the rules marked *review* and the part of a rule that its
   Kept-by cell leaves to review.
3. Answer each *review* rule with yes or no, and cite a file and line for each no.
4. Reproduce a defect before reporting it, or label it suspected. A suspected finding cannot make a verdict
   **Needs work**. Show the code that reaches it, or grade it Edge.
5. Answer the questions below, a sentence or two each.

The questions look past single rules:

- What would a newcomer find hardest to grasp, and why?
- Which part is more complex than the problem it solves?
- Which type, layer, option or test could be deleted without loss?
- Where do two parts do the same job in different ways?
- Which job does every host still write by hand in the same way?
- Which rule of CONVENTIONS.md is missing, or costs more than it catches?

## Closing a release

A release review ends with a bar to clear, not with a review that finds nothing.

- A finding of **Medium** or above holds the release. A **Low** or **Edge** finding, a broken rule among them, goes to
  `BACKLOG.md` and leaves its category at **Ship and file**.
- After the findings that hold it are fixed, a follow-up review reads only the diff of those fixes, and checks that each
  fix is right and breaks nothing. A finding in code the fixes did not touch is filed.
- The release ships when the follow-up review finds nothing that holds it.

## The report

- Each category gets its verdict and at most five findings, most severe first.
- A finding names its severity, its rule id or its defect, the file and line, how it was checked, and the fix.
- Suggestions go in a separate list, ready for `BACKLOG.md`, and the answers to the questions follow it.
