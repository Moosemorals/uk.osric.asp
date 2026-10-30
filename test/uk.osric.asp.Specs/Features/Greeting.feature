Feature: Greeting a visitor
  As the owner of this site
  I want visitors to be greeted by name when they give one
  So that the site feels personal without being intrusive

  Scenario: Visitor without a name
    When I ask for a greeting without a name
    Then I am greeted with "Hello, world!"

  Scenario: Visitor with a name
    When I ask for a greeting as "Ada"
    Then I am greeted with "Hello, Ada!"

  Scenario: Visitor with a padded name
    When I ask for a greeting as "  Ada  "
    Then I am greeted with "Hello, Ada!"
