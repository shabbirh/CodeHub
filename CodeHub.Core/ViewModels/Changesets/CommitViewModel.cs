using System;
using System.Reactive.Linq;
using CodeHub.Core.Services;
using CodeHub.Core.ViewModels.Repositories;
using CodeHub.Core.ViewModels.Source;
using ReactiveUI;
using System.Reactive;
using System.Linq;
using CodeHub.Core.Factories;
using CodeHub.Core.Utilities;
using GitHubSharp.Models;

namespace CodeHub.Core.ViewModels.Changesets
{
    public class CommitViewModel : BaseViewModel, ILoadableViewModel
    {
		public string Node { get; set; }

		public string RepositoryOwner { get; set; }

		public string RepositoryName { get; set; }

        public bool ShowRepository { get; set; }

        private Octokit.GitHubCommit _commitModel;
        public Octokit.GitHubCommit Commit
        {
            get { return _commitModel; }
            private set { this.RaiseAndSetIfChanged(ref _commitModel, value); }
        }

        private readonly ObservableAsPropertyHelper<string> _commitMessageSummary;
        public string CommitMessageSummary
        {
            get { return _commitMessageSummary.Value; }
        }

        private readonly ObservableAsPropertyHelper<string> _commitMessage;
        public string CommitMessage
        {
            get { return _commitMessage.Value; }
        }

        private readonly ObservableAsPropertyHelper<string> _commiterName;
        public string CommiterName
        {
            get { return _commiterName.Value; }
        }

        private readonly ObservableAsPropertyHelper<int> _diffAdditions;
        public int DiffAdditions
        {
            get { return _diffAdditions.Value; }
        }

        private readonly ObservableAsPropertyHelper<int> _diffDeletions;
        public int DiffDeletions
        {
            get { return _diffDeletions.Value; }
        }

        private readonly ObservableAsPropertyHelper<int> _diffModifications;
        public int DiffModifications
        {
            get { return _diffModifications.Value; }
        }

        public IReactiveCommand<Unit> LoadCommand { get; private set; }

        public IReactiveCommand<object> GoToRepositoryCommand { get; private set; }

        public IReactiveCommand<object> GoToHtmlUrlCommand { get; private set; }

        public IReactiveCommand<object> GoToAddedFiles { get; private set; }

        public IReactiveCommand<object> GoToRemovedFiles { get; private set; }

        public IReactiveCommand<object> GoToModifiedFiles { get; private set; }

        public IReactiveCommand<object> GoToAllFiles { get; private set; }

        public IReactiveCommand AddCommentCommand { get; private set; }

        public IReadOnlyReactiveList<CommitCommentItemViewModel> Comments { get; private set; }

        public IReactiveCommand GoToUrlCommand { get; private set; }

        public IReactiveCommand<Unit> ShowMenuCommand { get; private set; }
        
        public CommitViewModel(ISessionService applicationService, IActionMenuFactory actionMenuService, IAlertDialogFactory alertDialogFactory)
        {
            Title = "Commit";

            var comments = new ReactiveList<CommentModel>();
            Comments = comments.CreateDerivedCollection(x => new CommitCommentItemViewModel(x));

            this.WhenAnyValue(x => x.Commit.Files)
                .Select(x => x.Count(y => string.Equals(y.Status, "added")))
                .ToProperty(this, x => x.DiffAdditions, out _diffAdditions);

            this.WhenAnyValue(x => x.Commit.Files)
                .Select(x => x.Count(y => string.Equals(y.Status, "removed")))
                .ToProperty(this, x => x.DiffDeletions, out _diffDeletions);

            this.WhenAnyValue(x => x.Commit.Files)
                .Select(x => x.Count(y => string.Equals(y.Status, "modified")))
                .ToProperty(this, x => x.DiffModifications, out _diffModifications);

            GoToAddedFiles = ReactiveCommand.Create(this.WhenAnyValue(x => x.DiffAdditions).Select(x => x > 0));
            GoToAddedFiles
                .Select(_ => new CommitFilesViewModel())
                .Select(y => y.Init(RepositoryOwner, RepositoryName, Node, "Added", Commit.Files.Where(x => string.Equals(x.Status, "added"))))
                .Subscribe(NavigateTo);

            GoToRemovedFiles = ReactiveCommand.Create(this.WhenAnyValue(x => x.DiffDeletions).Select(x => x > 0));
            GoToRemovedFiles
                .Select(_ => new CommitFilesViewModel())
                .Select(y => y.Init(RepositoryOwner, RepositoryName, Node, "Removed", Commit.Files.Where(x => string.Equals(x.Status, "removed"))))
                .Subscribe(NavigateTo);

            GoToModifiedFiles = ReactiveCommand.Create(this.WhenAnyValue(x => x.DiffModifications).Select(x => x > 0));
            GoToModifiedFiles
                .Select(_ => new CommitFilesViewModel())
                .Select(y => y.Init(RepositoryOwner, RepositoryName, Node, "Modified", Commit.Files.Where(x => string.Equals(x.Status, "modified"))))
                .Subscribe(NavigateTo);

            GoToAllFiles = ReactiveCommand.Create(this.WhenAnyValue(x => x.Commit.Files).Select(x => x != null));
            GoToAllFiles
                .Select(_ => new CommitFilesViewModel())
                .Select(y => y.Init(RepositoryOwner, RepositoryName, Node, "All Changes", Commit.Files))
                .Subscribe(NavigateTo);

            this.WhenAnyValue(x => x.Commit)
                .IsNotNull()
                .Select(x => x.GenerateCommiterName())
                .ToProperty(this, x => x.CommiterName, out _commiterName);

            this.WhenAnyValue(x => x.Commit)
                .IsNotNull()
                .Select(x => x.Commit.Message ?? string.Empty)
                .Select(x => Emojis.FindAndReplace(x))
                .ToProperty(this, x => x.CommitMessage, out _commitMessage);

            this.WhenAnyValue(x => x.CommitMessage)
                .IsNotNull()
                .Select(x => {
                    var firstNewLine = x.IndexOf("\n", StringComparison.Ordinal);
                    return firstNewLine > 0 ? x.Substring(0, firstNewLine) : x;   
                })
                .ToProperty(this, x => x.CommitMessageSummary, out _commitMessageSummary);

            GoToHtmlUrlCommand = ReactiveCommand.Create(this.WhenAnyValue(x => x.Commit).Select(x => x != null));
            GoToHtmlUrlCommand
                .Select(_ => this.CreateViewModel<WebBrowserViewModel>())
                .Select(x => x.Init(Commit.HtmlUrl))
                .Subscribe(NavigateTo);
      
            GoToRepositoryCommand = ReactiveCommand.Create();
            GoToRepositoryCommand.Subscribe(_ =>
            {
                var vm = this.CreateViewModel<RepositoryViewModel>();
                vm.RepositoryOwner = RepositoryOwner;
                vm.RepositoryName = RepositoryName;
                NavigateTo(vm);
            });

            AddCommentCommand = ReactiveCommand.Create().WithSubscription(_ => {
                var vm = new ComposerViewModel(async s => {
                    var request = applicationService.Client.Users[RepositoryOwner].Repositories[RepositoryName].Commits[Node].Comments.Create(s);
                    comments.Add((await applicationService.Client.ExecuteAsync(request)).Data);
                }, alertDialogFactory);
                NavigateTo(vm);
            });

            var validCommitObservable = this.WhenAnyValue(x => x.Commit).Select(x => x != null);

            var copyShaCommand = ReactiveCommand.Create(validCommitObservable)
                .WithSubscription(x => actionMenuService.SendToPasteBoard(this.Commit.Sha));

            var shareCommand = ReactiveCommand.Create(validCommitObservable)
                .WithSubscription(sender => actionMenuService.ShareUrl(sender, this.Commit.HtmlUrl));

            var browseCodeCommand = ReactiveCommand.Create(validCommitObservable)
                .WithSubscription(x => 
                {
                    var vm = this.CreateViewModel<SourceTreeViewModel>();
                    vm.RepositoryName = RepositoryName;
                    vm.RepositoryOwner = RepositoryOwner;
                    vm.Branch = this.Commit.Sha;
                    NavigateTo(vm);
                });

            ShowMenuCommand = ReactiveCommand.CreateAsyncTask(sender => {
                var menu = actionMenuService.Create();
                menu.AddButton("Add Comment", AddCommentCommand);
                menu.AddButton("Copy SHA", copyShaCommand);
                menu.AddButton("Browse Code", browseCodeCommand);
                menu.AddButton("Share", shareCommand);
                menu.AddButton("Show in GitHub", GoToHtmlUrlCommand);
                return menu.Show(sender);
            });

            LoadCommand = ReactiveCommand.CreateAsyncTask(async t => {
                var commentRequest = applicationService.Client.Users[RepositoryOwner].Repositories[RepositoryName].Commits[Node].Comments.GetAll();
                applicationService.Client.ExecuteAsync(commentRequest).ToBackground(x => comments.Reset(x.Data.Where(y => y.Position.HasValue)));
                Commit = await applicationService.GitHubClient.Repository.Commits.Get(RepositoryOwner, RepositoryName, Node);
            });
        }
    }
}

