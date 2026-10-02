Comment very lightly. Comments should be 2 lines of text, max. Vast majority of code does not need commenting, only
stuff that's highly non-obvious. If comments are more than 10% of total code files, some are probably superfluous.

C# file sizes should typically be under 1,200 lines. Once you exceed 1,200 lines in one file, take a hard look at what
the file is doing: it's probably more than one job OR it's one job but it includes a bunch of repeated patterns/utility
code that are written inline and can be broken out into their own small utility methods in their own file(s).

Write code that is unit-testable. Keep complex logic out of the UI code, move it a layer down.

Tests should be kept fast so running the test suite doesn't slow down iterative development. Minimize IO during tests as
it can be a slowdown source.

If you are chatting with the user directly, strongly consider using sub-agents for implementation and review. These may
be Opus or Sonnet (GPT Sol or Luna) depending on task complexity. This will allow you to keep your own context
clean as you discuss topics with the user. Tell the sub-agent it is a sub-agent so it can understand that it's not
chatting with the user.

If you yourself have been launched as a sub-agent, do the assigned task, do not delegate further.

All commits should get a review. The reviewing agent should be a separate sub-agent, NEVER the same agent with context
who just implemented the change. If the size of the diff is only a few thousand tokens and won't clutter context, the
managing agent may review it rather than spawning a fresh sub-agent, but the implementer cannot be relied upon to
self-review its own work. The reviewer must be a sufficiently smart model. Sonnet may implement, but Opus/GPT Sol must
review.
