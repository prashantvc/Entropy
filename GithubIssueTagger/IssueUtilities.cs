﻿using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GithubIssueTagger
{
    internal partial class IssueUtilities
    {
        public static async Task GetIssuesRankedAsync(GitHubClient client, params string[] labels)
        {
            var issues = await GetIssuesForLabels(client, "NuGet", "Home", labels);
            var allIssues = new List<IssueRankingModel>(issues.Count);

            var internalAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "nkolev92",
                "donnie-msft",
                "dominofire",
                "erdembayar",
                "zivkan",
                "clairernovotny",
                "erdembayar",
                "jeffkl",
                "rrelyea",
                "jebriede",
                "dtivel",
                "heng-liu",
                "kartheekp-ms",
                "aortiz-msft",
                "zkat",
                "emgarten",
                "patobeltran",
                "rohit21agrawal",
                "zhili1208",
                "jainaashish",
                "cristinamanum"
            };

            foreach (var issue in issues)
            {
                var score = await CalculateScoreAsync(issue, client, internalAliases);

                allIssues.Add(new IssueRankingModel(issue, score));
            }

            var markdownTable = allIssues.OrderByDescending(e => e.Score).ToMarkdownTable(GetModelMapping());

            Console.WriteLine();
            Console.WriteLine(markdownTable);
            Console.WriteLine();
            Console.ReadKey();

            static List<Tuple<string, string>> GetModelMapping()
            {
                return new List<Tuple<string, string>>()
                {
                    new Tuple<string, string>("Link", "Link"),
                    new Tuple<string, string>("Title", "Title"),
                    new Tuple<string, string>("Assignee", "Assignee"),
                    new Tuple<string, string>("Milestone", "Milestone"),
                    new Tuple<string, string>("Score", "Score"),
                };
            }
            static async Task<double> CalculateScoreAsync(Issue issue, GitHubClient client, HashSet<string> internalAliases)
            {
                int totalCommentsCount = issue.Comments;
                int reactionCount = issue.Reactions.TotalCount;

                IReadOnlyList<IssueComment> issueComments = await client.Issue.Comment.GetAllForIssue("NuGet", "Home", issue.Number);

                List<string> allCommenters = issueComments.Select(e => e.User.Login).ToList();
                List<string> uniqueCommenterList = allCommenters.Distinct<string>().Where(e => e.Equals(issue.User.Login)).ToList();
                int uniqueCommentersCount = uniqueCommenterList.Count;

                var internalCommentersCount = uniqueCommenterList.Where(e => internalAliases.Contains(e) && !internalAliases.Contains(issue.User.Login)).Count();

                return uniqueCommentersCount + reactionCount - (internalCommentersCount * 0.25) + CaculateExtraCommentImpact(totalCommentsCount, uniqueCommentersCount);
                static double CaculateExtraCommentImpact(int totalComments, int uniqueCommenters)
                {
                    int diff = totalComments - uniqueCommenters;

                    var tens = Math.Max(Math.Min(diff - 10, 10), 0) * 0.25;
                    var twenties = Math.Max(Math.Min(diff - 20, 10), 0) * 0.10;
                    var thirties = Math.Max(diff - 30, 0) * 0.05;

                    return tens + twenties + thirties;
                }

            }
        }

        public static async Task<IList<Issue>> GetIssuesForMilestone(GitHubClient client, string org, string repo, string milestone, Predicate<Issue> predicate)
        {
            var shouldPrioritize = new RepositoryIssueRequest
            {
                Milestone = milestone,
                Filter = IssueFilter.All,
            };

            var issuesForMilestone = await client.Issue.GetAllForRepository(org, repo, shouldPrioritize);

            return issuesForMilestone.Where(e => predicate(e)).ToList();
        }

        public static async Task<IList<Issue>> GetIssuesForLabel(GitHubClient client, string org, string repo, string label)
        {
            var issuesForMilestone = await GetAllIssues(client, org, repo);
            return issuesForMilestone.Where(e => HasLabel(e, label)).ToList();
        }

        // All labels need to be considered.
        public static async Task<IList<Issue>> GetIssuesForLabels(GitHubClient client, string org, string repo, params string[] labels)
        {
            var issuesForMilestone = await GetAllIssues(client, org, repo);
            return issuesForMilestone.Where(e => labels.All(label => HasLabel(e, label))).ToList();
        }

        public static async Task<IEnumerable<Issue>> GetAllIssues(GitHubClient client, string org, string repo)
        {
            var shouldPrioritize = new RepositoryIssueRequest
            {
                Filter = IssueFilter.All
            };

            var issuesForMilestone = await client.Issue.GetAllForRepository(org, repo, shouldPrioritize);
            return issuesForMilestone;
        }

        public static async Task<IEnumerable<Issue>> GetOpenPriority1Issues(GitHubClient client, string org, string repo)
        {
            var nugetRepos = new RepositoryCollection();
            nugetRepos.Add(org, repo);

            var queryLabels = new string[] { "priority:1" };

            var request = new SearchIssuesRequest()
            {
                Repos = nugetRepos,
                State = ItemState.Open,
                Labels = queryLabels
            };
            var issuesForMilestone = await client.Search.SearchIssues(request);
            return issuesForMilestone.Items;
        }

        /// <summary>
        /// Get all the issues considered unprocessed. This means that either the issue does not have any labels, or only has the pipeline labels.
        /// </summary>
        public static async Task<IList<Issue>> GetUnprocessedIssues(GitHubClient client, string org, string repo)
        {
            var shouldPrioritize = new RepositoryIssueRequest
            {
                Filter = IssueFilter.All
            };

            var issuesForMilestone = await client.Issue.GetAllForRepository(org, repo, shouldPrioritize);

            return issuesForMilestone.Where(e => IsUnprocessed(e)).ToList();

            static bool IsUnprocessed(Issue e)
            {
                return e.Labels.Count == 0 || e.Labels.All(e => e.Name.StartsWith("Pipeline"));
            }
        }

        public static async Task AddLabelToMatchingIssues(GitHubClient client, string label, string org, string repo, Predicate<Issue> predicate)
        {
            var issuesForRepo = await client.Issue.GetAllForRepository(org, repo);

            foreach (var issue in issuesForRepo)
            {
                if (predicate(issue))
                {
                    try
                    {
                        var issueUpdate = issue.ToUpdate();
                        issueUpdate.AddLabel(label);
                        await client.Issue.Update(org, repo, issue.Number, issueUpdate);
                        Console.WriteLine($"Updated issue: {issue.HtmlUrl}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Unhandled issue {issue.HtmlUrl} {e}");
                    }
                }
            }
        }

        public static async Task RemoveLabelFromAllIssuesAsync(GitHubClient client, string label, string org, string repo)
        {
            var issuesForRepo = await client.Issue.GetAllForRepository(org, repo);

            foreach (var issue in issuesForRepo)
            {
                if (issue.Labels != null)
                {
                    if (issue.Labels.Any(e => e.Name.Equals(label)))
                    {
                        try
                        {
                            var issueUpdate = issue.ToUpdate();
                            issueUpdate.RemoveLabel(label);
                            await client.Issue.Update(org, repo, issue.Number, issueUpdate);
                            Console.WriteLine($"Updated issue: {issue.HtmlUrl}");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Unhandled issue {issue.HtmlUrl} {e}");
                        }
                    }
                }
            }
        }

        private static bool HasLabel(Issue e, string label)
        {
            return e.Labels.Any(e => e.Name.Equals(label));
        }
    }

    public class IssueRankingModel
    {
        public string Link { get; }
        public string Title { get; }
        public string Assignee { get; }
        public string Milestone { get; }
        public double Score { get; }

        public IssueRankingModel(Issue e, double score)
        {
            Link = e.HtmlUrl;
            Title = e.Title;
            Assignee = string.Join(",", e.Assignees.Select(e => e.Login));
            Milestone = e.Milestone?.Title;
            Score = score;
        }
    }
}
