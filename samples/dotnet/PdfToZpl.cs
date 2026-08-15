public async Task<string> PdfToZpl(string pdfPath)
{
	using (var client = new HttpClient())
	{
		client.BaseAddress = new Uri("https://api.labelzoom.com");
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "<YOUR AUTH TOKEN HERE>");
		using (var fileStream = File.OpenRead(pdfPath))
		using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2.5/convert/pdf/to/zpl"))
		using (var content = new StreamContent(fileStream))
		{
			content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
			request.Content = content;
			using (var response = await client.SendAsync(request))
			{
				response.EnsureSuccessStatusCode();
				return await response.Content.ReadAsStringAsync();
			}
		}
	}
}