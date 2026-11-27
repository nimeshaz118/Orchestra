from dotenv import load_dotenv  # type: ignore[reportMissingImports]
import os
from langchain_core.prompts import PromptTemplate  # type: ignore[reportMissingImports]
from langchain_nvidia_ai_endpoints import ChatNVIDIA  # type: ignore[reportMissingImports]

load_dotenv()


def main():


    information = """Sri Lanka,[a] officially the Democratic Socialist Republic of Sri Lanka, formerly known as Ceylon,[b] is an island country in South Asia. It is located in the Indian Ocean, southwest of the Bay of Bengal, and is separated from India by the Gulf of Mannar and the Palk Strait. Sri Lanka shares a maritime border with the Maldives to the southwest and India to the northwest, and lies across the Bay of Bengal from Bangladesh and Myanmar to the northeast and the Andaman and Nicobar Islands of India to the east. Its executive and judicial capital is Colombo, which is also its largest city and the political, financial and cultural centre of the country; neighboring Sri Jayawardenepura Kotte is the legislative capital. Sri Lanka's population is 22 million. About 74% of the population are Sinhalese, who speak the Sinhala language, and about 15% are Tamils who speak the Tamil language. Other long-established ethnic groups include the Moors, Indian Tamils, Burghers, Malays, Chinese, and Vedda.[16]

        The island has a documented history of over 3,000 years, with evidence of prehistoric human settlement dating back 125,000 years.[17] Sri Lanka has been given various names throughout its long history, with Ceylon most notably being used prior to its independence in 1948.[18] The earliest known Buddhist writings in the island, known collectively as the Pali Canon, date back to 29 BCE.[19][20] Sri Lanka, owing to its strategic geographical location, played a role as a major trading hub, and was well known to explorers across the world as early as the Anuradhapura period. The Portuguese Empire established a colony in the sixteenth century, during a period of political upheaval in the Kingdom of Kotte where it also faced attacks from neighbouring kingdoms of Kandy and Sitawaka. Following the Sinhalese–Portuguese War, the Dutch colonial empire controlled the coastal areas. By the early 19th century, the British Empire established a colony on the island, which lasted until 1948. The early 20th century saw the rise of nationalist movements and increasing calls for independence. In 1948, Sri Lanka gained independence as the Dominion of Ceylon, eventually becoming a republic in 1972. Sri Lanka's recent history has been marred by a prolonged civil war between the Tamil separatist militant organisation of Liberation Tigers of Tamil Eelam and the Sri Lankan Armed Forces, which lasted from 1983 to 2009, and ended with the defeat of the separatists.[21]

        In the 21st century, Sri Lanka has emerged as a developing country, with a strategically important geographical location in the Indian Ocean, with its deep harbours giving it significant geopolitical leverage as a major trading post from the ancient Maritime Silk Road.[22][23][24] It has the highest level of human development in South Asia and the second-highest GDP per capita in the region. Sri Lanka has a long history of international engagement and cooperation, and is a member of several international organisations—including SAARC, the G77, the Non-Aligned Movement and the Commonwealth of Nations.

        """
    summeryTemplate = """
        Summerize the information {information} about a country i want you to create:
            1. A short summary of the country in 2-3 sentences.
            2. A list of 2 interesting facts about the country.
    """
    summeryPromptTemplate = PromptTemplate(
        input_variables=["information"],
        template=summeryTemplate
        )

    llm = ChatNVIDIA(
        # base_url="https://integrate.api.nvidia.com/v1",
        model="nvidia/nemotron-3.5-super-vl-120b-a12b",
        api_key=os.environ["NVIDIA_API_KEY"]
        )

    chain = summeryPromptTemplate | llm

    response = chain.invoke({"information": information})

    print(response.content)

if __name__ == "__main__":
    main()
