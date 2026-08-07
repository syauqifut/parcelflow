# AI Usage

## Tools

- Cursor Ask mode (primary)
- Cursor Agent mode (primary)
- ChatGPT (occasional)

## How I used AI

### Cursor Ask Mode
Used as my primary discussion partner throughout the assessment.

Typical usage included:
- Understanding the existing architecture and project structure.
- Discussing implementation approaches before coding.
- Reviewing code changes.
- Explaining unfamiliar C#/.NET concepts.
- Troubleshooting build and compilation errors.

### Cursor Agent
Used to implement changes after I had decided on an approach.

Typical usage included:
- Editing multiple files.
- Refactoring repetitive changes.
- Generating boilerplate code.
- Assisting with test implementation.

All generated code was reviewed and adjusted before being committed.

### ChatGPT
Used occasionally outside the project for general discussion and learning.

Typical usage included:
- Clarifying assignment requirements.
- Discussing design trade-offs.
- Improving documentation wording.
- Refreshing .NET/C# concepts.

## Verification

I reviewed all AI-generated suggestions before accepting them. Verification included:
- Reading the existing codebase.
- Building and running the application.
- Running the test suite.
- Comparing report results with MongoDB where applicable.

## Example where AI was wrong

- AI suggested the initial aggregation query for the weekly driver summary.
- The suggestion was based on code only and had no access to the actual database.
- I validated the generated report against MongoDB using Studio 3T.
- The report values did not match the stored data.
- After identifying the mismatch, I discussed the issue with AI and refined the query.
- Verified the final query against MongoDB.