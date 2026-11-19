// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>One choice a question offers.</summary>
/// <param name="Label">What the option is called, and what the answer records.</param>
/// <param name="Description">Why the owner would pick it, or null.</param>
public sealed record OwnerQuestionOption(string Label, string? Description);

/// <summary>One question the assistant asks its owner.</summary>
/// <param name="Question">The question, in one sentence.</param>
/// <param name="Header">A few words naming the question, or null.</param>
/// <param name="Options">The choices offered; empty when the answer is free text.</param>
/// <param name="MultiSelect">Whether more than one option may be chosen.</param>
public sealed record OwnerQuestion(
    string Question,
    string? Header,
    IReadOnlyList<OwnerQuestionOption> Options,
    bool MultiSelect);

/// <summary>The questions one tool call puts to the owner, in the order they are asked.</summary>
/// <param name="Questions">The questions; this is the interrupt's stored request.</param>
public sealed record OwnerQuestionSet(IReadOnlyList<OwnerQuestion> Questions);

/// <summary>What the owner gave for one question.</summary>
/// <param name="Question">The question as it was asked, so an answer reads without the record.</param>
/// <param name="Header">The question's header as it was asked, or null.</param>
/// <param name="Selected">The option labels chosen; empty when the owner typed instead or skipped.</param>
/// <param name="Text">The owner's own words; empty when they chose instead or skipped.</param>
public sealed record OwnerAnswer(string Question, string? Header, IReadOnlyList<string> Selected, string Text);

/// <summary>Everything the owner answered, in the order the questions were asked.</summary>
/// <param name="Answers">One entry per question, empty where it was skipped.</param>
public sealed record OwnerAnswerSet(IReadOnlyList<OwnerAnswer> Answers);

/// <summary>What the owner submitted for one question, before it is checked against the question.</summary>
/// <param name="Selected">The labels the form submitted.</param>
/// <param name="Text">The owner's own words.</param>
public sealed record OwnerAnswerInput(IReadOnlyList<string> Selected, string Text);

/// <summary>A set of answers, or the refusal that stopped it being recorded.</summary>
/// <param name="Answers">The answers, or null when an input was refused.</param>
/// <param name="Error">A refusal that names the question and what is wrong, or null.</param>
public sealed record OwnerAnswersResult(OwnerAnswerSet? Answers, string? Error) {
    /// <summary>Whether the inputs became a set of answers.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>The question and answer shapes, their bounds and their stored form.</summary>
/// <remarks>
/// The tool validates the questions, the route builds the answers and the page renders both, so the
/// shapes live here once: a cap or a refusal cannot drift between what the model asked, what the
/// owner gave and what the page shows. A question the owner leaves blank is an answer like any
/// other — <c>selected: []</c> and <c>text: ""</c> — because the run waited and the card was shown.
/// </remarks>
public static class OwnerQuestions {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes a question set as the interrupt request stores it.</summary>
    /// <param name="questions">The questions to write.</param>
    /// <returns>The stored JSON.</returns>
    public static string Write(OwnerQuestionSet questions) {
        ArgumentNullException.ThrowIfNull(questions);

        return JsonSerializer.Serialize(questions, s_jsonOptions);
    }

    /// <summary>Reads a stored question set, or null when the JSON is not one this build understands.</summary>
    /// <param name="json">The stored request.</param>
    /// <remarks>A record written with a null where the shape has a value is read as an empty one, so a
    /// hand-written or older row is a card with less on it rather than an exception on the page.</remarks>
    public static OwnerQuestionSet? Read(string? json) {
        if (Read<OwnerQuestionSet>(json) is not { Questions: { } questions }) {
            return null;
        }

        return new OwnerQuestionSet([.. questions.Select(question => question with {
            Question = question.Question ?? string.Empty,
            Options = question.Options ?? [],
        })]);
    }

    /// <summary>Writes a set of answers as the interrupt resolution stores it.</summary>
    /// <param name="answers">The answers to write.</param>
    /// <returns>The stored JSON.</returns>
    public static string WriteAnswers(OwnerAnswerSet answers) {
        ArgumentNullException.ThrowIfNull(answers);

        return JsonSerializer.Serialize(answers, s_jsonOptions);
    }

    /// <summary>Reads a stored set of answers, or null when the JSON is not one this build understands.</summary>
    /// <param name="json">The stored resolution.</param>
    public static OwnerAnswerSet? ReadAnswers(string? json) {
        if (Read<OwnerAnswerSet>(json) is not { Answers: { } answers }) {
            return null;
        }

        return new OwnerAnswerSet([.. answers.Select(answer => answer with {
            Question = answer.Question ?? string.Empty,
            Selected = answer.Selected ?? [],
            Text = answer.Text ?? string.Empty,
        })]);
    }

    /// <summary>Checks a question set against the configured caps.</summary>
    /// <param name="questions">The questions the model sent.</param>
    /// <param name="maximumQuestions">The most questions one call may ask.</param>
    /// <param name="maximumOptions">The most options one question may offer.</param>
    /// <param name="maximumChars">The longest question, header, label or option description.</param>
    /// <returns>A refusal naming the cap and the accepted shape, or null when the set is valid.</returns>
    public static string? Validate(
        OwnerQuestionSet questions,
        int maximumQuestions,
        int maximumOptions,
        int maximumChars) {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQuestions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        if (questions.Questions is null || questions.Questions.Count == 0) {
            return "Ask at least one question.";
        }
        if (questions.Questions.Count > maximumQuestions) {
            return $"Ask at most {Count(maximumQuestions)} questions in one call; this call had {Count(questions.Questions.Count)}.";
        }

        for (int index = 0; index < questions.Questions.Count; index++) {
            string number = Count(index + 1);
            OwnerQuestion question = questions.Questions[index];
            if (string.IsNullOrWhiteSpace(question.Question)) {
                return $"Question {number} needs text.";
            }
            if (question.Question.Length > maximumChars) {
                return LongRefusal($"Question {number}", question.Question.Length, maximumChars);
            }
            if (question.Header is { } header) {
                if (string.IsNullOrWhiteSpace(header)) {
                    return $"Question {number}'s header needs text.";
                }
                if (header.Length > maximumChars) {
                    return LongRefusal($"Question {number}'s header", header.Length, maximumChars);
                }
            }
            IReadOnlyList<OwnerQuestionOption> options = question.Options ?? [];
            if (options.Count == 1) {
                return $"Question {number} offers 1 option; an option list needs between 2 and {Count(maximumOptions)}.";
            }
            if (options.Count > maximumOptions) {
                return $"Question {number} offers {Count(options.Count)} options; the limit is {Count(maximumOptions)}.";
            }

            HashSet<string> labels = [with(StringComparer.Ordinal)];
            foreach (OwnerQuestionOption option in options) {
                if (string.IsNullOrWhiteSpace(option.Label)) {
                    return $"Question {number} has an option with no label.";
                }
                if (option.Label.Length > maximumChars) {
                    return LongRefusal($"Question {number}'s option label", option.Label.Length, maximumChars);
                }
                if (!labels.Add(option.Label)) {
                    return $"Question {number} offers the label \"{option.Label}\" twice.";
                }
                if (option.Description is { Length: > 0 } description && description.Length > maximumChars) {
                    return LongRefusal($"Question {number}'s option description", description.Length, maximumChars);
                }
            }
        }

        return null;
    }

    /// <summary>Builds the answer set the owner submitted, in the order the questions were asked.</summary>
    /// <param name="questions">The questions the owner answered.</param>
    /// <param name="inputs">One input per question, in the same order.</param>
    /// <param name="maximumChars">
    /// The most characters the chosen labels and typed text may add up to, which is the owner
    /// message's share of the context budget.
    /// </param>
    /// <returns>The answers, or the refusal that names what was wrong with the submission.</returns>
    public static OwnerAnswersResult Answer(
        OwnerQuestionSet questions,
        IReadOnlyList<OwnerAnswerInput> inputs,
        long maximumChars) {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumChars);

        if (questions.Questions is null || inputs.Count != questions.Questions.Count) {
            return new OwnerAnswersResult(null, "Every question needs one answer.");
        }

        List<OwnerAnswer> answers = [];
        long characters = 0;
        for (int index = 0; index < questions.Questions.Count; index++) {
            string number = Count(index + 1);
            OwnerQuestion question = questions.Questions[index];
            OwnerAnswerInput input = inputs[index];
            IReadOnlyList<OwnerQuestionOption> options = question.Options ?? [];
            List<string> selected = [];
            foreach (string label in input.Selected ?? []) {
                if (!options.Any(option => string.Equals(option.Label, label, StringComparison.Ordinal))) {
                    return new OwnerAnswersResult(null, $"Question {number} does not offer \"{label}\".");
                }
                if (!question.MultiSelect && selected.Count == 1) {
                    return new OwnerAnswersResult(null, $"Question {number} takes one choice.");
                }
                if (!selected.Contains(label, StringComparer.Ordinal)) {
                    selected.Add(label);
                }
                characters += label.Length;
            }

            string text = input.Text ?? string.Empty;
            characters += text.Length;
            answers.Add(new OwnerAnswer(question.Question, question.Header, selected, text));
        }

        if (characters > maximumChars) {
            return new OwnerAnswersResult(null,
                $"The answer is {Count(characters)} characters; the limit is {Count(maximumChars)}.");
        }

        return new OwnerAnswersResult(new OwnerAnswerSet(answers), null);
    }

    private static string LongRefusal(string what, int length, int maximumChars) =>
        $"{what} is {Count(length)} characters; the limit is {Count(maximumChars)}.";

    private static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static T? Read<T>(string? json) where T : class {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<T>(json, s_jsonOptions);
        } catch (JsonException) {
            return null;
        }
    }
}
