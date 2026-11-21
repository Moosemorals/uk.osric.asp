<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Security reviews

A review written down rather than assumed. Each one names the date it read the code, the scheme it
read, what it found with a severity and a resolution, and the risks it accepted with the reason. A
review is a record of one reading, not a standing guarantee: the code it names may have moved since,
and the [decisions](../decisions.md) are what hold a rule in place.

## Contents

- [Auth cookie review](auth-cookie-review.md) — the authentication cookie and `StoredTicketDataFormat`
  against the framework's handler: two narrow key-lifecycle races, fixed, and four accepted risks.
