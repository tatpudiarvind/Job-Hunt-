# Gmail Integration

The Google OAuth connection is limited to the Gmail API `gmail.compose` scope. It permits creating drafts and sending messages through the connected Gmail account. It does not request Google Drive, Calendar, Contacts, profile, or broad Google-account permissions, and it does not read the mailbox.

Gmail read access must not be added without an explicit product decision and a separate scope review. Refresh tokens are encrypted locally using ASP.NET Core Data Protection before they are persisted.