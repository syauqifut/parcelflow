Overview

Part A - Cross-tenant Report Bug
- Reproduce bug, check with tenant id `nusantara-express`
- Open the database using Studio 3T
- Recheck collection `delivery_tasks`
- Check the code, specially in middleware
- Found the bug
- Fix it, in Report Service, tenant id not implemented yet


Part B
- Check current workflow
- Adding two new enum: Return Scheduled and Returned
- Choose this because: it is last mile, just between terminal and recipient
- Adding notification to recipient and opt

Part C
- Check the current report, result:
    - Daily is already accesible by tenant's opt with API
- Make a API to generate weekly report
- Check availability and ease of installation of report, csv or xlsx
    - Choose csv because more simple
- Check the query againt the data in database
- Update logic and query, then validate againt database
- Because this is API, it cant be fully automate like generate csv and emailed to opt