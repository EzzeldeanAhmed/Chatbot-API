using System.ComponentModel;
using Chatbot_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Chatbot_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ChatbotController : Controller
    {
        private readonly ChatOptions chatOptions = new();
        private readonly List<ChatMessage> messages = new();
        private CancellationTokenSource? currentResponseCancellation;
        private ChatMessage? currentResponseMessage;
        private IChatClient? _chatClient;
        private SemanticSearch _semanticSearch;
        private const string SystemPrompt = @"You are an AI assistant for an Art Museum Gallery application. Your role is to assist users by providing accurate, helpful information about artworks, artists, and museum topics.
Rules:
- When a user mentions an artist or artwork, first search the provided data to check if it exists.
- If the artist or artwork is found in the provided data, prioritize this information in your response.
- If the artist, artwork, or topic is not found in the provided data, you may provide general art knowledge while clearly indicating: ""This artwork/artist is not in our museum collection, but I can share some general information...""
- For general art history questions, you may provide informative responses based on your knowledge.
- If the user asks about topics not related to art, artists, museums, or art history, respond: ""I'm here to assist you with information about art and our museum's collection. Please ask me something related!""
- Always maintain a professional, friendly, and respectful tone, like a museum guide.

Reminder:
- Clearly distinguish between information from the museum's database and general art knowledge in your responses.";

        //When you do this, end your
        //reply with citations in the special XML format:
        //<citation filename='string' page_number='number'>exact quote here</citation>
        //Always include the citation in your response if there are results.
        //  The quote must be max 5 words, taken word-for-word from the search result, and is the basis for why the citation is relevant.
        //Don't refer to the presence of citations; just emit these tags right at the end, with no surrounding text.
    
        public ChatbotController(IChatClient? chatClient, SemanticSearch semanticSearch)
        {
            messages.Add(new(ChatRole.System, SystemPrompt));
            chatOptions.Tools = [AIFunctionFactory.Create(SearchAsync)];
            this._chatClient = chatClient;
            this._semanticSearch = semanticSearch;
        }


        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] RequestBody requestBody)
        {
            //CancelAnyCurrentResponse();
            var userPrompt = requestBody.UserPrompt;
            ChatMessage chatMessage = new(ChatRole.User, [new TextContent(userPrompt)]);

            // Add the user message to the conversation
            messages.Add(chatMessage);

            // Stream and display a new response from the IChatClient
            var responseText = new TextContent("");
            currentResponseMessage = new ChatMessage(ChatRole.Assistant, [responseText]);
            currentResponseCancellation = new();
            await foreach (var chunk in _chatClient.GetStreamingResponseAsync(messages, chatOptions, currentResponseCancellation.Token))
            {
                responseText.Text += chunk.Text;
            }

            // Store the final response in the conversation, and begin getting suggestions
            messages.Add(currentResponseMessage!);
            currentResponseMessage = null;

            // Return the response as JSON
            return Ok(responseText.Text);
        }

        public class RequestBody {
            public string UserPrompt { set; get; }
        }

        [Description("Searches for information using a phrase or keyword")]
        private async Task<IEnumerable<string>> SearchAsync(
        [Description("The phrase to search for.")] string searchPhrase,
        [Description("Whenever possible, specify the filename to search that file only. If not provided, the search includes all files.")] string? filenameFilter = null)
        {
            var results = await _semanticSearch.SearchAsync(searchPhrase, filenameFilter, maxResults: 5);
            return results.Select(result =>
                $"<result filename=\"{result.FileName}\" page_number=\"{result.PageNumber}\">{result.Text}</result>");
        }
    }
}
