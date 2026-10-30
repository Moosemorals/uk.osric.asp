# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

Feature: C# source compilation
  The compiler turns valid C# source into a dynamically linked library

  Scenario: Compile valid source
    Given the following C# source:
      """
      public class Generated { }
      """
    When the source is compiled
    Then the compilation succeeds

  Scenario: Reject invalid source
    Given the following C# source:
      """
      public class Generated {
      """
    When the source is compiled
    Then the compilation fails