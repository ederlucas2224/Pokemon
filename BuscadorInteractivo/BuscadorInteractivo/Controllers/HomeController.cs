using BuscadorInteractivo.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace BuscadorInteractivo.Controllers
{
	public class HomeController : Controller
	{
		private readonly IHttpClientFactory _httpClientFactory;

		public HomeController(IHttpClientFactory httpClientFactory)
		{
			_httpClientFactory = httpClientFactory;
		}

		[HttpGet]
		public IActionResult Buscar()
		{
			ViewBag.Loading = false;
			ViewBag.Error = null;
			ViewBag.Pokemon = null;
			return View(new SearchModel());
		}

		[HttpPost]
		public async Task<IActionResult> Buscar(SearchModel model)
		{
			ViewBag.Loading = true;
			ViewBag.Error = null;
			ViewBag.Pokemon = null;

			if (string.IsNullOrEmpty(model.Nombre))
			{
				ViewBag.Error = "Por favor ingresa un nombre.";
				ViewBag.Loading = false;
				return View(model);
			}

			var client = _httpClientFactory.CreateClient();
			var url = $"https://pokeapi.co/api/v2/pokemon/{model.Nombre.ToLower()}";

			try
			{
				var response = await client.GetAsync(url);
				var apiResult = await response.Content.ReadAsStringAsync();

				if (!response.IsSuccessStatusCode)
				{
					ViewBag.Error = "No se encontró el Pokémon o hubo un error en la búsqueda.";
					ViewBag.Loading = false;
					return View(model);
				}

				var json = JsonDocument.Parse(apiResult).RootElement;

				var pokemon = new
				{
					Name = json.GetProperty("name").GetString(),
					Id = json.GetProperty("id").GetInt32(),
					Sprites = json.GetProperty("sprites").GetProperty("front_default").GetString(),
					Types = json.GetProperty("types").EnumerateArray()
						.Select(t => t.GetProperty("type").GetProperty("name").GetString()).ToList(),
					Weight = json.GetProperty("weight").GetInt32(),
					Height = json.GetProperty("height").GetInt32(),
					Stats = json.GetProperty("stats").EnumerateArray()
						.Select(s => new {
							Name = s.GetProperty("stat").GetProperty("name").GetString(),
							Base = s.GetProperty("base_stat").GetInt32()
						}).ToList()
				};
				ViewBag.Pokemon = pokemon;
			}
			catch (Exception)
			{
				ViewBag.Error = "Ocurrió un error al consultar el API.";
			}

			ViewBag.Loading = false;
			return View(model);
		}

		public async Task<IActionResult> Pokedex(string type = "")
		{
			var client = _httpClientFactory.CreateClient();
			var pokemons = new List<PokedexPokemon>();
			var typesList = new List<string>();

			// Obtener tipos para el filtro
			var typesRes = await client.GetAsync("https://pokeapi.co/api/v2/type");
			if (typesRes.IsSuccessStatusCode)
			{
				var typesJson = JsonDocument.Parse(await typesRes.Content.ReadAsStringAsync());
				typesList = typesJson.RootElement.GetProperty("results")
					.EnumerateArray()
					.Select(t => t.GetProperty("name").GetString())
					.Where(n => n != "unknown" && n != "shadow")
					.ToList();
			}

			int limit = 20;
			int offset = 0;

			if (string.IsNullOrEmpty(type))
			{
				// Sin filtro: obtenemos primeros 20 Pokémon
				var res = await client.GetAsync($"https://pokeapi.co/api/v2/pokemon?limit={limit}&offset={offset}");
				var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
				foreach (var poke in json.RootElement.GetProperty("results").EnumerateArray())
				{
					var pokeRes = await client.GetAsync(poke.GetProperty("url").GetString());
					var pokeJson = JsonDocument.Parse(await pokeRes.Content.ReadAsStringAsync());
					pokemons.Add(new PokedexPokemon
					{
						Name = pokeJson.RootElement.GetProperty("name").GetString(),
						Img = pokeJson.RootElement.GetProperty("sprites").GetProperty("front_default").GetString(),
						Types = pokeJson.RootElement.GetProperty("types").EnumerateArray()
							.Select(t => t.GetProperty("type").GetProperty("name").GetString()).ToList()
					});
				}
			}
			else
			{
				// Con filtro: obtenemos los primeros 20 Pokémon del tipo seleccionado
				var res = await client.GetAsync($"https://pokeapi.co/api/v2/type/{type.ToLower()}");
				if (res.IsSuccessStatusCode)
				{
					var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
					var allPokemonsByType = json.RootElement.GetProperty("pokemon")
						.EnumerateArray()
						.Take(limit);

					foreach (var pokeEntry in allPokemonsByType)
					{
						var pokeUrl = pokeEntry.GetProperty("pokemon").GetProperty("url").GetString();
						var pokeRes = await client.GetAsync(pokeUrl);
						var pokeJson = JsonDocument.Parse(await pokeRes.Content.ReadAsStringAsync());

						pokemons.Add(new PokedexPokemon
						{
							Name = pokeJson.RootElement.GetProperty("name").GetString(),
							Img = pokeJson.RootElement.GetProperty("sprites").GetProperty("front_default").GetString(),
							Types = pokeJson.RootElement.GetProperty("types").EnumerateArray()
								.Select(t => t.GetProperty("type").GetProperty("name").GetString()).ToList()
						});
					}
				}
			}

			ViewBag.Types = typesList;
			ViewBag.Offset = offset;
			ViewBag.Limit = limit;
			ViewBag.SelectedType = type;
			return View(pokemons);
		}


		[HttpGet]
		public async Task<JsonResult> PokedexData(int offset = 0, int limit = 20, string type = "")
		{
			var client = _httpClientFactory.CreateClient();
			var pokemons = new List<PokedexPokemon>();

			try
			{
				if (string.IsNullOrEmpty(type))
				{
					// Sin filtro por tipo: usamos la paginación nativa de la PokeAPI
					var res = await client.GetAsync($"https://pokeapi.co/api/v2/pokemon?limit={limit}&offset={offset}");
					if (!res.IsSuccessStatusCode)
						return Json(pokemons); // devuelves vacío si hay error

					var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
					foreach (var poke in json.RootElement.GetProperty("results").EnumerateArray())
					{
						var pokeUrl = poke.GetProperty("url").GetString();
						var pokeRes = await client.GetAsync(pokeUrl);
						if (!pokeRes.IsSuccessStatusCode)
							continue;

						var pokeJson = JsonDocument.Parse(await pokeRes.Content.ReadAsStringAsync());

						pokemons.Add(new PokedexPokemon
						{
							Name = pokeJson.RootElement.GetProperty("name").GetString(),
							Img = pokeJson.RootElement.GetProperty("sprites").GetProperty("front_default").GetString(),
							Types = pokeJson.RootElement.GetProperty("types").EnumerateArray()
								.Select(t => t.GetProperty("type").GetProperty("name").GetString()).ToList()
						});
					}
				}
				else
				{
					// Con filtro por tipo: la API devuelve TODO, y tú haces paginación en memoria
					var res = await client.GetAsync($"https://pokeapi.co/api/v2/type/{type.ToLower()}");
					if (!res.IsSuccessStatusCode)
						return Json(pokemons); // devuelves vacío si hay error

					var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

					var allPokemonsByType = json.RootElement.GetProperty("pokemon")
						.EnumerateArray()
						.Skip(offset)
						.Take(limit);

					foreach (var pokeEntry in allPokemonsByType)
					{
						var pokeUrl = pokeEntry.GetProperty("pokemon").GetProperty("url").GetString();
						var pokeRes = await client.GetAsync(pokeUrl);
						if (!pokeRes.IsSuccessStatusCode)
							continue;

						var pokeJson = JsonDocument.Parse(await pokeRes.Content.ReadAsStringAsync());

						pokemons.Add(new PokedexPokemon
						{
							Name = pokeJson.RootElement.GetProperty("name").GetString(),
							Img = pokeJson.RootElement.GetProperty("sprites").GetProperty("front_default").GetString(),
							Types = pokeJson.RootElement.GetProperty("types").EnumerateArray()
								.Select(t => t.GetProperty("type").GetProperty("name").GetString()).ToList()
						});
					}
				}

				return Json(pokemons);
			}
			catch (Exception ex)
			{
				// Retornar lista vacía si algo falla
				return Json(pokemons);
			}
		}


		[HttpGet]
		public async Task<JsonResult> PokedexDetails(string name)
		{
			var client = _httpClientFactory.CreateClient();
			var res = await client.GetAsync($"https://pokeapi.co/api/v2/pokemon/{name}");
			var pokeJson = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
			return Json(pokeJson.RootElement);
		}

		public IActionResult Index()
		{
			return RedirectToAction("Buscar");
		}
	}
}