// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

// Every test drives one host process and one shared database, and a browser per test; running them
// beside each other would only make the pages contend for the same assistant queue and inflate the
// wall clock the streaming tests measure.
[assembly: NonParallelizable]
